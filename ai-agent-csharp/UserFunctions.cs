using System.Text.Json;

namespace Agent
{
    public static class UserFunctions
    {
        public static string SubmitSupportTicket(string emailAddress, string description)
        {
            var ticketNumber = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var fileName = $"ticket-{ticketNumber}.txt";
            var scriptDir = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(scriptDir, fileName);

            File.WriteAllText(filePath,
                $"Support ticket: {ticketNumber}\nSubmitted by: {emailAddress}\nDescription:\n{description}");

            DemoDb.Tickets[ticketNumber] = new DemoDb.Ticket
            {
                Id = ticketNumber,
                Email = emailAddress,
                Description = description
            };

            var response = new { ticketId = ticketNumber, fileName, message = $"Support ticket {ticketNumber} submitted." };
            return JsonSerializer.Serialize(response);
        }

        public static string CheckTicketStatus(string ticketId)
        {
            if (!DemoDb.Tickets.TryGetValue(ticketId, out var t))
                return JsonSerializer.Serialize(new { found = false, message = "Ticket not found" });

            return JsonSerializer.Serialize(new { found = true, ticketId = t.Id, status = t.Status, priority = t.Priority, comments = t.Comments, createdAtUtc = t.CreatedAtUtc });
        }

        public static string AddTicketComment(string ticketId, string comment)
        {
            if (!DemoDb.Tickets.TryGetValue(ticketId, out var t))
                return JsonSerializer.Serialize(new { ok = false, message = "Ticket not found" });

            t.Comments.Add(comment);
            return JsonSerializer.Serialize(new { ok = true, ticketId, totalComments = t.Comments.Count });
        }

        public static string SetTicketPriority(string ticketId, int priority)
        {
            if (!DemoDb.Tickets.TryGetValue(ticketId, out var t))
                return JsonSerializer.Serialize(new { ok = false, message = "Ticket not found" });

            t.Priority = priority;
            return JsonSerializer.Serialize(new { ok = true, ticketId, priority });
        }

        public static string EscalateTicket(string ticketId, string reason)
        {
            if (!DemoDb.Tickets.TryGetValue(ticketId, out var t))
                return JsonSerializer.Serialize(new { ok = false, message = "Ticket not found" });

            t.Status = "escalated";
            t.Comments.Add($"[ESCALATED] {reason}");
            return JsonSerializer.Serialize(new { ok = true, ticketId, status = t.Status });
        }

        public static string ListUserTickets(string email)
        {
            var items = DemoDb.Tickets.Values
                .Where(t => t.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
                .Select(t => new { t.Id, t.Status, t.Priority, t.CreatedAtUtc })
                .OrderByDescending(t => t.CreatedAtUtc)
                .Take(20)
                .ToList();

            return JsonSerializer.Serialize(new { count = items.Count, tickets = items });
        }

        private static readonly (string title, string[] tags, string answer)[] Kb = new[] {
            ("Reset PC in Safe Mode", new[]{"boot","freeze","startup"}, "Hold Shift while clicking Restart -> Troubleshoot -> Advanced Options -> Startup Settings."),
            ("Office repair", new[]{"word","office","crash"}, "Control Panel -> Programs -> Microsoft 365 -> Change -> Online Repair."),
            ("Update graphics drivers", new[]{"gpu","freeze"}, "Device Manager -> Display adapters -> Update driver.")
        };

        public static string SearchKb(string query)
        {
            var hits = Kb.Where(k =>
                k.title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.tags.Any(tag => query.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                .Select(k => new { k.title, k.answer })
                .Take(3)
                .ToList();

            return JsonSerializer.Serialize(new { results = hits });
        }

        public static string SendEmailNotification(string to, string subject, string body)
        {
            // Mock: write an .eml-like file to disk for the demo
            var id = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"outgoing-{id}.txt");
            File.WriteAllText(file, $"TO: {to}\nSUBJECT: {subject}\n\n{body}");
            return JsonSerializer.Serialize(new { sent = true, file });
        }


    }
}
