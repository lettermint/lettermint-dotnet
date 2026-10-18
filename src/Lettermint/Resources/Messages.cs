using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>
/// Sent and received messages. Needs the team token; <see cref="RescheduleAsync"/>,
/// <see cref="CancelAsync"/> and <see cref="ResendAsync"/> also accept the sending
/// token when no team token is configured.
/// </summary>
public sealed class Messages : ApiResource
{
    internal Messages(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists messages, one page at a time.</summary>
    public Task<ListMessagesResponse> ListAsync(ListMessagesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListMessages, "Messages.ListAsync", [], query, default, options, cancellationToken);

    /// <summary>Iterates over every message, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<MessageListData> IterateAsync(ListMessagesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListMessagesQuery, ListMessagesResponse, MessageListData>(Operations.ListMessages, "Messages.IterateAsync", [], query, options, cancellationToken);

    /// <summary>Retrieves a message.</summary>
    public Task<MessageData> RetrieveAsync(string messageId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetMessage, "Messages.RetrieveAsync", [messageId], default, default, options, cancellationToken);

    /// <summary>Lists the events of a message, one page at a time.</summary>
    public Task<ListMessageEventsResponse> EventsAsync(string messageId, ListMessageEventsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListMessageEvents, "Messages.EventsAsync", [messageId], query, default, options, cancellationToken);

    /// <summary>Iterates over every event of a message, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<MessageEventData> IterateEventsAsync(string messageId, ListMessageEventsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListMessageEventsQuery, ListMessageEventsResponse, MessageEventData>(Operations.ListMessageEvents, "Messages.IterateEventsAsync", [messageId], query, options, cancellationToken);

    /// <summary>The raw RFC 822 source.</summary>
    public Task<string> SourceAsync(string messageId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetMessageSource, "Messages.SourceAsync", [messageId], default, default, options, cancellationToken);

    /// <summary>The HTML body.</summary>
    public Task<string> HtmlAsync(string messageId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetMessageHtml, "Messages.HtmlAsync", [messageId], default, default, options, cancellationToken);

    /// <summary>The plain-text body.</summary>
    public Task<string> TextAsync(string messageId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetMessageText, "Messages.TextAsync", [messageId], default, default, options, cancellationToken);

    /// <summary>Moves a scheduled message to another delivery time. Uses the team token if configured, otherwise the sending token.</summary>
    public Task<ScheduledMessage> RescheduleAsync(string messageId, RescheduleMessageRequest body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.RescheduleMessage, "Messages.RescheduleAsync", [messageId], default, body, options, cancellationToken);

    /// <summary>Cancels a scheduled message. Uses the team token if configured, otherwise the sending token.</summary>
    public Task<ScheduledMessage> CancelAsync(string messageId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CancelScheduledMessage, "Messages.CancelAsync", [messageId], default, default, options, cancellationToken);

    /// <summary>
    /// Sends the stored content of a sent message again as a new message with its own ID.
    /// Without <paramref name="body"/> (or its <c>Recipients</c>) every original recipient
    /// receives it. Works while the content is retained (otherwise HTTP 410), at most 5 times
    /// in 24 hours. Uses the team token if configured, otherwise the sending token.
    /// </summary>
    /// <param name="messageId">The message to resend.</param>
    /// <param name="body">The recipients of the original message to resend to; <c>null</c> sends no body.</param>
    /// <param name="options">The idempotency key and timeout of this call.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<PendingSendMailResponse> ResendAsync(string messageId, ResendMessageRequest? body = null, IdempotentRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ResendMessage, "Messages.ResendAsync", [messageId], default, body, options, cancellationToken);

    /// <summary>Releases one quarantined inbound message for webhook delivery.</summary>
    public Task<ProcessInboundMessageResponse> ProcessAsync(string messageId, IdempotentRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ProcessInboundMessage, "Messages.ProcessAsync", [messageId], default, default, options, cancellationToken);
}
