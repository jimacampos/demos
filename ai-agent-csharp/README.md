# AI Agent POC — Support Desk Assistant (C#/.NET 6 + Azure OpenAI Assistants)

## Purpose & Scope

This POC demonstrates **Azure OpenAI Assistants** orchestrating **tool calls** in a realistic support‑desk flow. The agent collects issue details, creates a ticket, and supports a small lifecycle (status, comments, priority, escalation, listing user tickets), plus a tiny knowledge base lookup and a mocked email notification.

**Tech focus:** how the assistant is defined, how tools are registered (JSON schemas), how runs transition to `RequiresAction`, how arguments are deserialized, how tool outputs are submitted back, and how state is persisted for demo purposes.

---

## Components

* **`Program.cs`** — wires configuration, builds the Azure OpenAI **Assistant**, registers tools, manages the **run lifecycle**, and routes tool calls.
* **`UserFunctions.cs`** — implements the **tool functions** the assistant can invoke. Returns JSON so the assistant can cite concrete results.
* **`DemoDb.cs`** — an **in‑memory store** to keep tickets and enable stateful demos without external dependencies.

---

## High‑Level Flow

```
Console User → CreateMessage → CreateRun → (poll) →
  if RequiresAction → deserialize tool args → invoke C# function → SubmitToolOutputs →
  assistant completes → fetch latest assistant message → print
```

---

## Assistant Definition (Program.cs)

* **Name:** `support-agent`
* **Instructions (summary):** act as a support agent; collect email + description to create tickets; use specific tools for status, comments, priority, escalation; list tickets by email; search KB; optionally notify via email. Include guardrails (confirm critical fields, don’t invent IDs, only escalate with reason).
* **Model/endpoint:** read from `appsettings.json` (`PROJECT_ENDPOINT`, `MODEL_DEPLOYMENT_NAME`).
* **Creation:** `CreateAssistantAsync` → `CreateThreadAsync` → loop user prompts → `CreateRunAsync` → poll `GetRunAsync` until `Completed`/`RequiresAction`/`Failed`.

---

## Registered Tools (Function Definitions)

All tools are defined with **JSON Schema** for arguments and registered on the assistant. During `RequiresAction`, the program switches on `FunctionName`, executes, and returns a **JSON string** with results.

| Function name             | Purpose                                                    | Required args                                      | Typical return (JSON)                                             | Side effects                                             |
| ------------------------- | ---------------------------------------------------------- | -------------------------------------------------- | ----------------------------------------------------------------- | -------------------------------------------------------- |
| `submit_support_ticket`   | Create a new ticket and write a `.txt` file for demo proof | `email_address : string`, `description : string`   | `{ ticketId, fileName, message }`                                 | Persists ticket in memory and writes `ticket-XXXXXX.txt` |
| `check_ticket_status`     | Read ticket state                                          | `ticket_id : string`                               | `{ found, ticketId, status, priority, comments[], createdAtUtc }` | None                                                     |
| `add_ticket_comment`      | Append a comment                                           | `ticket_id : string`, `comment : string`           | `{ ok, ticketId, totalComments }`                                 | Mutates in‑memory ticket                                 |
| `set_ticket_priority`     | Update priority (1..5)                                     | `ticket_id : string`, `priority : int`             | `{ ok, ticketId, priority }`                                      | Mutates in‑memory ticket                                 |
| `escalate_ticket`         | Mark as escalated with reason                              | `ticket_id : string`, `reason : string`            | `{ ok, ticketId, status:"escalated" }`                            | Mutates in‑memory ticket                                 |
| `list_user_tickets`       | List tickets for an email                                  | `email_address : string`                           | `{ count, tickets:[{ Id, Status, Priority, CreatedAtUtc }] }`     | None                                                     |
| `search_kb`               | Tiny KB lookup (demo retrieval pattern)                    | `query : string`                                   | `{ results:[{ title, answer }] }`                                 | None                                                     |
| `send_email_notification` | Mocked external notification                               | `to : string`, `subject : string`, `body : string` | `{ sent:true, file:"outgoing-XXXX.txt" }`                         | Writes an `.txt` representing the email                  |

> **Why JSON responses?** Keeping responses strictly structured makes the assistant’s follow‑ups reliable (it can quote IDs, statuses, etc.).

---

## Tool Implementations (UserFunctions.cs)

* **Creation & persistence:** `SubmitSupportTicket` generates a 6‑char ID, writes a demo file, and stores the ticket in memory.
* **Stateful ops:** `CheckTicketStatus`, `AddTicketComment`, `SetTicketPriority`, `EscalateTicket`, and `ListUserTickets` read/update the in‑memory store.
* **KB search:** a toy corpus demonstrates a retrieval‑style helper without external infra.
* **Email mock:** writes a file shaped like an email for easy demo verification.

> In production you’d back these with a real DB and services (ticketing, SMTP, etc.). The signatures and JSON contracts can remain stable while the internals change.

---

## In‑Memory State (DemoDb.cs)

A minimal structure to support stateful demos:

* `Ticket { Id, Email, Description, Status, Priority, Comments[], CreatedAtUtc }`
* Dictionary keyed by `Id`.

This avoids any external setup while enabling realistic flows (create → check → update → escalate → list).

---

## Run Lifecycle (Step‑by‑Step)

1. **Input:** Read user prompt from console.
2. **Message:** `CreateMessageAsync(threadId, User, [MessageContent.FromText(...)])`.
3. **Run:** `CreateRunAsync(threadId, assistantId)`.
4. **Poll:** Repeat `GetRunAsync` until:

   * **Completed:** fetch latest assistant message (descending) and print.
   * **RequiresAction:**

     * Deserialize `toolCall.FunctionArguments` (`Dictionary<string, JsonElement>`).
     * `switch` on `toolCall.FunctionName` and call the C# function.
     * Collect `ToolOutput`s and `SubmitToolOutputsToRunAsync`.
   * **Failed:** log `LastError` and continue.
5. **History:** At exit, stream the full conversation ascending.
6. **Cleanup:** `DeleteAssistantAsync(assistantId)` when the app ends.

---

## Configuration & Running

1. Create `appsettings.json` with:

   ```json
   {
     "PROJECT_ENDPOINT": "https://<your‑resource>.openai.azure.com/",
     "MODEL_DEPLOYMENT_NAME": "<your‑deployment>"
   }
   ```
2. Build & run: `dotnet run`
3. Try these prompts (exercise all tools):

   * Create: “My email is ana@example.com. Word crashes when I paste images.”
   * Status: “What’s the status of ticket **AB12CD**?”
   * Comment: “Add this note to **AB12CD**: ‘Issue happens after updating to 2408.’”
   * Priority & Escalation: “Make **AB12CD** priority **1** and escalate. Reason: customer impact across finance.”
   * List: “List my tickets for ana@example.com.”
   * KB: “How do I fix a frozen screen on startup?”
   * Email: “Notify lead@example.com about **AB12CD** with subject ‘Escalated’ and include the reason.”

---

## Sample Prompts (Per Tool)

Use these natural‑language prompts to drive the agent toward each tool. They’re phrased the way users actually talk, not like API calls.

### Create ticket → `submit_support_ticket`

* "My email is **[ana@example.com](mailto:ana@example.com)** and Word crashes when I paste images."
* "**[mike@contoso.com](mailto:mike@contoso.com)** here. Laptop overheats after 10 minutes of video calls."
* "Issue: Teams won’t start; Email: **[dev@contoso.com](mailto:dev@contoso.com)**."

### Check status → `check_ticket_status`

* "What’s the status of ticket **AB12CD**?"
* "Is **F0E1D2** resolved yet?"

### Add comment → `add_ticket_comment`

* "Add this note to **AB12CD**: *Happens after updating to 2408*"
* "Attach a comment on **F0E1D2** saying *customer can reproduce on two devices*."

### Set priority → `set_ticket_priority`

* "Set **AB12CD** to priority **1**."
* "Lower **F0E1D2** to priority **4**."

### Escalate → `escalate_ticket`

* "Escalate **AB12CD**. Reason: *impacts finance month‑end close*."
* "Please escalate **F0E1D2** due to *security exposure*."

### List user tickets → `list_user_tickets`

* "List my tickets for **[ana@example.com](mailto:ana@example.com)**."
* "Show recent tickets for **[mike@contoso.com](mailto:mike@contoso.com)**."

### Search KB → `search_kb`

* "How do I fix a frozen screen on startup?"
* "Word crashes when pasting images — any known fix?"

### Send notification (mock) → `send_email_notification`

* "Email **[lead@example.com](mailto:lead@example.com)** about **AB12CD** with subject *Escalated ticket* and body *We escalated due to widespread impact*."
* "Notify **it‑[manager@contoso.com](mailto:manager@contoso.com)**: subject *Ticket F0E1D2 update*, body *priority lowered to 4*."

### Multi‑step demo (copy/paste this sequence)

1. "My email is **[sam@example.com](mailto:sam@example.com)**. Teams crashes on join."
2. "What’s the status of ticket **<ID returned above>**?"
3. "Add this note to **<ID>**: *Occurs after Windows update KB5030219*."
4. "Set **<ID>** to priority **1** and escalate. Reason: *Exec meeting at 2pm*."
5. "Email **helpdesk‑[lead@example.com](mailto:lead@example.com)** with subject *Escalated <ID>* and body *Please assist*."

### Error‑path prompts (agent should ask for missing bits)

* "My computer is broken." → expect the agent to ask for email + description.
* "Check status of **ZZZZZZ**" (non‑existent) → expect a *not found* response.

---

## Notes & Limitations

* **Secrets:** API key is hardcoded in this POC for simplicity; move it to configuration or environment before sharing code.
* **Persistence:** DemoDb is volatile; restart loses state.
* **Schemas:** Keep JSON schemas strict; the agent relies on them to call the right tool with correct types.
* **Idempotency:** For real systems, guard against duplicate creates and validate inputs server‑side.

---

## What This Proves

* Assistants can reliably **decide when to call tools** and pass **well‑typed** arguments.
* Tools can return **structured JSON** that the assistant uses for accurate follow‑ups.
* A small state layer makes the agent feel “real” without deploying external services.
