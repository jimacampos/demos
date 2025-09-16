using System.Text.Json;

namespace Agent
{
    public static class UserFunctions
    {
        /// <summary>
        /// Submit a support ticket with email and description
        /// </summary>
        /// <param name="emailAddress">The email address of the user</param>
        /// <param name="description">Description of the issue</param>
        /// <returns>JSON response with ticket information</returns>
        public static string SubmitSupportTicket(string emailAddress, string description)
        {
            // Generate a unique ticket number (6 characters)
            var ticketNumber = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 6);

            // Create file name and path
            var fileName = $"ticket-{ticketNumber}.txt";
            var scriptDir = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(scriptDir, fileName);

            // Create ticket content
            var text = $"Support ticket: {ticketNumber}\n" +
                      $"Submitted by: {emailAddress}\n" +
                      $"Description:\n{description}";

            // Write to file
            File.WriteAllText(filePath, text);

            // Create response message
            var response = new
            {
                message = $"Support ticket {ticketNumber} submitted. The ticket file is saved as {fileName}"
            };

            return JsonSerializer.Serialize(response);
        }
    }
}
