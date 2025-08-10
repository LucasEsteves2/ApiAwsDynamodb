using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using JobManager.API.Entities;
using JobManager.API.Persistence;
using JobManager.API.Persistence.Models;
using JobManager.API.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SendGrid.Extensions.DependencyInjection;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        //builder.Services.AddDbContext<AppDbContext>(o =>
        //    o.UseInMemoryDatabase("AppDb"));

        builder.Services.AddHostedService<JobApplicationCreatedSubscriber>();

        var connectionString = builder.Configuration.GetConnectionString("AppDb");

        builder.Services.AddDbContext<AppDbContext>(o =>
            o.UseSqlServer(connectionString));

        // S3
        var s3 = new AmazonS3Client(RegionEndpoint.USEast1);

        builder.Services.AddSingleton<IAmazonS3>(s3);

        // SQS
        var sqs = new AmazonSQSClient(RegionEndpoint.USEast1);

        builder.Services.AddSingleton<IAmazonSQS>(sqs);

        // DynamoDB
        var dynamoDb = new AmazonDynamoDBClient(RegionEndpoint.USEast1);

        builder.Services.AddSingleton<IAmazonDynamoDB>(dynamoDb);

        builder.Services.AddSingleton<IDynamoDBContext, DynamoDBContext>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.MapPost("/jobs", 
            async (Job job, AppDbContext db) =>
        {
            db.Jobs.Add(job);

            await db.SaveChangesAsync();

            return Results.Created($"/jobs/{job.Id}", job);
        });

        app.MapGet("/jobs", async (AppDbContext db) =>
        {
            var jobs = await db.Jobs.ToListAsync();

            return Results.Ok(jobs);
        });

        app.MapPost("/jobs/{id:int}/apply", 
            async (
                int id, 
                JobApplication application,  
                AppDbContext db, 
                IAmazonSQS sqs,
                IConfiguration config) =>
        {
            var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == id);

            if (job is null) return Results.NotFound("Job not found.");

            application.JobId = id;

            await db.JobApplications.AddAsync(application);

            await db.SaveChangesAsync();

            var queueUrl = config["AWS:SQSQueueUrl"];

            var body = $"New application for Job {job.Title} and ID {job.Id}: {application.CandidateName}|{application.CandidateEmail}";

            var request = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = body
            };

            var response = await sqs.SendMessageAsync(request);

            return Results.Ok(application);
        });

        app.MapPost("/job-applications/{id:int}/upload-cv", 
            async (int id, IAmazonS3 s3, IFormFile file, AppDbContext db, IConfiguration config) =>
        {
            if (file == null || file.Length == 0)
                return Results.BadRequest("Invalid file.");

            var application = await db.JobApplications.SingleOrDefaultAsync(a => a.Id == id);

            if (application is null) return Results.NotFound();

            var bucketName = config["AWS:S3BucketName"];

            var objectKey = $"job-applications/{id}-{file.FileName}";

            using var stream = file.OpenReadStream();

            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                InputStream = stream
            };

            var response = await s3.PutObjectAsync(request);

            application.CVUrl = objectKey;

            await db.SaveChangesAsync();

            var fileUrl = $"https://{bucketName}.s3.amazonaws.com/{objectKey}";

            return Results.Ok(new { FileUrl = fileUrl });
        }).DisableAntiforgery();

        app.MapPost("/v2/jobs",
            async (Job job, IDynamoDBContext db) =>
            {
                var dbModel = new JobDbModel(job);

                await db.SaveAsync(dbModel);

                return Results.Created($"/jobs/{job.Id}", job);
            });

        app.MapGet("/v2/jobs", async (IDynamoDBContext db) =>
        {
            var jobs = await db.ScanAsync<JobDbModel>([]).GetRemainingAsync();

            return Results.Ok(jobs);
        });

        app.MapPost("/v2/jobs/{id}/apply",
            async (
                Guid id,
                JobApplicationDbModel application,
                IDynamoDBContext db,
                IAmazonSQS sqs,
                IConfiguration config) =>
            {
                var job = await db.LoadAsync<JobDbModel>(id.ToString());

                if (job is null) return Results.NotFound("Job not found.");

                job.Applications.Add(application);

                await db.SaveAsync(job);

                var queueUrl = config["AWS:SQSQueueUrl"];

                var body = $"New application for Job {job.Title} and ID {job.Id}: {application.CandidateName}|{application.CandidateEmail}";

                var request = new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = body
                };

                var response = await sqs.SendMessageAsync(request);

                return Results.Ok(application);
            });

        app.Run();
    }
}