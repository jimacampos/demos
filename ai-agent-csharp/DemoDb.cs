namespace Agent
{
    public static class DemoDb
    {
        public static readonly Dictionary<string, Ticket> Tickets = new();
        public class Ticket
        {
            public string Id { get; set; } = default!;
            public string Email { get; set; } = default!;
            public string Description { get; set; } = default!;
            public string Status { get; set; } = "open";
            public List<string> Comments { get; set; } = new();
            public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
            public int Priority { get; set; } = 3; // 1=high, 5=low
        }
    }
}
