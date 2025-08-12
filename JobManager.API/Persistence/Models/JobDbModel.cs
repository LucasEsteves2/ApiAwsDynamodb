using Amazon.DynamoDBv2.DataModel;
using JobManager.API.Entities;

namespace JobManager.API.Persistence.Models
{
    [DynamoDBTable("Jobs")]
    public class JobDbModel
    {
        public JobDbModel()
        {
            
        }
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public decimal MinSalary { get; set; }
        public decimal MaxSalary { get; set; }
        public string Company { get; set; }
        [DynamoDBProperty("Applications")]
        public List<JobApplicationDbModel> Applications { get; set; }

        public JobDbModel(Job job)
        {
            Id = Guid.NewGuid().ToString();
            Title = job.Title;
            Description = job.Description;
            MinSalary = job.MinSalary;
            MaxSalary = job.MaxSalary;
            Company = job.Company;
            Applications = [];
        }
    }

    public class JobApplicationDbModel
    {
        public string CandidateName { get; set; }
        public string CandidateEmail { get; set; }
        public string? CVUrl { get; set; }
    }
}
