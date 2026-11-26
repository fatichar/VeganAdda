using VeganAdda.Contracts;

namespace VeganAdda.Web.Data;

public sealed class ProfileReminderBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdminId { get; set; }
    public Guid RequestId { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? Completed { get; set; }
    public int RetryVersion { get; set; }
    public List<ProfileReminderDelivery> Deliveries { get; set; } = [];
}

public sealed class ProfileReminderDelivery
{
    public Guid BatchId { get; set; }
    public Guid MemberId { get; set; }
    public ProfileReminderStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public Guid? LeaseId { get; set; }
}
