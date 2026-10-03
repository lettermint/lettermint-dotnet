using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>
/// Sends email with the project sending token (<c>x-lettermint-token</c>).
/// Holds no message state: every call sends exactly what it is given.
/// </summary>
public sealed class Emails : ApiResource
{
    internal Emails(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Sends one email.</summary>
    /// <param name="message">The email, in the API's format.</param>
    /// <param name="options">The idempotency key and timeout of this call.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<SendMailResponse> SendAsync(SendMailRequest message, IdempotentRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Transport.AssertAuth("Emails.SendAsync", AuthSurface.Sending);
        MessageValidation.Validate(message, string.Empty);
        return Transport.CallAsync(Operations.SendMail, "Emails.SendAsync", [], default, message, options, cancellationToken);
    }

    /// <summary>Sends up to 500 emails in one request.</summary>
    public Task<IReadOnlyList<SendMailResponse>> SendBatchAsync(IEnumerable<SendMailRequest> messages, IdempotentRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Transport.AssertAuth("Emails.SendBatchAsync", AuthSurface.Sending);
        if (messages is null)
        {
            throw new LettermintValidationException("SendBatchAsync takes a list of messages.", "messages");
        }
        var list = messages.ToList();
        for (var index = 0; index < list.Count; index++)
        {
            MessageValidation.Validate(list[index], $"messages[{index}]");
        }
        return Transport.CallAsync(Operations.SendBatchMail, "Emails.SendBatchAsync", [], default, (IReadOnlyList<SendMailRequest>)list, options, cancellationToken);
    }

    /// <summary>Sends up to 500 emails, built with <see cref="Compose()"/>, in one request.</summary>
    public Task<IReadOnlyList<SendMailResponse>> SendBatchAsync(IEnumerable<EmailBuilder> builders, IdempotentRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (builders is null)
        {
            throw new LettermintValidationException("SendBatchAsync takes a list of builders.", "messages");
        }
        return SendBatchAsync(builders.Select(builder => builder.Build()), options, cancellationToken);
    }

    /// <summary>
    /// Starts an immutable email builder. Every setter returns a new builder and
    /// leaves the current one unchanged.
    /// </summary>
    public EmailBuilder Compose()
    {
        Transport.AssertAuth("Emails.Compose", AuthSurface.Sending);
        return new EmailBuilder(this, EmailBuilder.Empty);
    }

    /// <summary>Starts an immutable email builder from an existing message.</summary>
    public EmailBuilder Compose(SendMailRequest message)
    {
        Transport.AssertAuth("Emails.Compose", AuthSurface.Sending);
        MessageValidation.Validate(message, string.Empty);
        return new EmailBuilder(this, message);
    }

    /// <summary>Checks the sending token: <c>GET /ping</c> returns <c>pong</c>.</summary>
    public async Task<string> PingAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var text = await Transport.CallAsync(Operations.Ping, "Emails.PingAsync", [], default, default, options, cancellationToken, AuthSurface.Sending).ConfigureAwait(false);
        return text.Trim();
    }
}

/// <summary>
/// An immutable email builder, created by <see cref="Emails.Compose()"/>.
/// </summary>
/// <remarks>
/// Every setter returns a new builder and leaves the current one unchanged, so a
/// base builder can be shared and reused safely, also across concurrent sends.
/// A setter that throws leaves the builder unchanged. <c>To</c>, <c>Cc</c>,
/// <c>Bcc</c> and <c>ReplyTo</c> replace their list; <c>Attach</c> appends.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class EmailBuilder
{
    internal static readonly SendMailRequest Empty = new() { From = string.Empty, To = [], Subject = string.Empty };

    private readonly Emails _emails;
    private readonly SendMailRequest _message;

    internal EmailBuilder(Emails emails, SendMailRequest message)
    {
        _emails = emails;
        _message = message;
    }

    private EmailBuilder With(SendMailRequest message)
    {
        MessageValidation.Validate(message, string.Empty);
        return new EmailBuilder(_emails, message);
    }

    /// <summary>Sender, for example <c>Acme &lt;hello@acme.com&gt;</c>.</summary>
    public EmailBuilder From(string address) => With(_message with { From = address });

    /// <summary>Replaces the recipients.</summary>
    public EmailBuilder To(params string[] addresses) => With(_message with { To = [.. addresses] });

    /// <summary>Replaces the CC recipients.</summary>
    public EmailBuilder Cc(params string[] addresses) => With(_message with { Cc = [.. addresses] });

    /// <summary>Replaces the BCC recipients.</summary>
    public EmailBuilder Bcc(params string[] addresses) => With(_message with { Bcc = [.. addresses] });

    /// <summary>Replaces the Reply-To addresses.</summary>
    public EmailBuilder ReplyTo(params string[] addresses) => With(_message with { ReplyTo = [.. addresses] });

    /// <summary>The subject line.</summary>
    public EmailBuilder Subject(string subject) => With(_message with { Subject = subject });

    /// <summary>The HTML body. <c>null</c> removes it.</summary>
    public EmailBuilder Html(string? html) => With(_message with { Html = html is null ? Optional<string?>.Unset : html });

    /// <summary>The plain-text body. <c>null</c> removes it.</summary>
    public EmailBuilder Text(string? text) => With(_message with { Text = text is null ? Optional<string?>.Unset : text });

    /// <summary>Replaces the custom email headers (not HTTP headers).</summary>
    public EmailBuilder Headers(IReadOnlyDictionary<string, string> headers) => With(_message with { Headers = new Dictionary<string, string>(headers) });

    /// <summary>Replaces the metadata (tracked with the email, not added as headers).</summary>
    public EmailBuilder Metadata(IReadOnlyDictionary<string, string> metadata) => With(_message with { Metadata = new Dictionary<string, string>(metadata) });

    /// <summary>The legacy single tag. <c>null</c> removes it.</summary>
    public EmailBuilder Tag(string? tag) => With(_message with { Tag = tag is null ? Optional<string?>.Unset : tag });

    /// <summary>Replaces the name/value tags (up to 20, or 19 with a legacy tag).</summary>
    public EmailBuilder Tags(params MessageTagInput[] tags) => With(_message with { Tags = [.. tags] });

    /// <summary>Replaces the name/value tags (up to 20, or 19 with a legacy tag).</summary>
    public EmailBuilder Tags(IEnumerable<MessageTagInput> tags) => With(_message with { Tags = tags?.ToArray()! });

    /// <summary>The route slug to send through. <c>null</c> removes it.</summary>
    public EmailBuilder Route(string? route) => With(_message with { Route = route });

    /// <summary>Schedules delivery: ISO 8601 or English such as <c>tomorrow 9am</c>. <c>null</c> removes it.</summary>
    public EmailBuilder ScheduledAt(string? when) => With(_message with { ScheduledAt = when });

    /// <summary>Schedules delivery at an instant, sent as ISO 8601 in UTC.</summary>
    public EmailBuilder ScheduledAt(DateTimeOffset when) =>
        With(_message with { ScheduledAt = when.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) });

    /// <summary>Per-email settings that override the route settings.</summary>
    public EmailBuilder Settings(SendMailRequestSettings? settings) => With(_message with { Settings = settings });

    /// <summary>The result a Sandbox project simulates for every recipient.</summary>
    public EmailBuilder SandboxResult(SandboxResult? result) => With(_message with { SandboxResult = result });

    /// <summary>Adds an attachment.</summary>
    public EmailBuilder Attach(EmailAttachment attachment)
    {
        if (attachment is null)
        {
            throw new LettermintValidationException("Attach takes an EmailAttachment.", "attachments");
        }
        return With(_message with { Attachments = [.. _message.Attachments ?? [], attachment.ToInput()] });
    }

    /// <summary>Adds an attachment from raw bytes; the SDK base64-encodes them.</summary>
    public EmailBuilder Attach(string filename, ReadOnlySpan<byte> content, string? contentType = null, string? contentId = null) =>
        Attach(new EmailAttachment(filename, content) { ContentType = contentType, ContentId = contentId });

    /// <summary>Returns the message in the API's format.</summary>
    public SendMailRequest Build() => _message;

    /// <summary>Sends a snapshot of this email. The builder stays unchanged and can be sent again.</summary>
    public Task<SendMailResponse> SendAsync(IdempotentRequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _emails.SendAsync(_message, options, cancellationToken);

    /// <summary>The message; contains no credentials.</summary>
    public override string ToString() => "EmailBuilder " + _message;
}

/// <summary>
/// An attachment for <see cref="EmailBuilder.Attach(EmailAttachment)"/>. Content is
/// raw bytes (base64-encoded by the SDK) or base64 text.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class EmailAttachment
{
    private readonly string _base64;

    /// <summary>An attachment from raw bytes.</summary>
    public EmailAttachment(string filename, ReadOnlySpan<byte> content)
    {
        Filename = CheckFilename(filename);
        _base64 = Convert.ToBase64String(content);
    }

    private EmailAttachment(string filename, string base64)
    {
        Filename = CheckFilename(filename);
        _base64 = base64 ?? throw new LettermintValidationException("Attachment content must not be null.", "attachments");
    }

    /// <summary>An attachment from base64-encoded content.</summary>
    public static EmailAttachment FromBase64(string filename, string base64Content) => new(filename, base64Content);

    /// <summary>The file name.</summary>
    public string Filename { get; }

    /// <summary>MIME type, for example <c>application/pdf</c>. Detected by the API when null.</summary>
    public string? ContentType { get; init; }

    /// <summary>Content-ID for inline images referenced as <c>cid:&lt;ContentId&gt;</c> in the HTML.</summary>
    public string? ContentId { get; init; }

    internal MessageAttachmentInput ToInput()
    {
        var input = new MessageAttachmentInput { Filename = Filename, Content = _base64 };
        if (ContentType is not null)
        {
            input = input with { ContentType = ContentType };
        }
        if (ContentId is not null)
        {
            input = input with { ContentId = ContentId };
        }
        return input;
    }

    private static string CheckFilename(string filename) =>
        string.IsNullOrEmpty(filename) ? throw new LettermintValidationException("An attachment needs a filename.", "attachments") : filename;

    /// <summary>The file name and content type.</summary>
    public override string ToString() => $"EmailAttachment {{ Filename = {Filename}, ContentType = {ContentType}, ContentId = {ContentId} }}";
}

/// <summary>
/// Checks what the SDK can check before a request: the tags and the attachments.
/// Shared by <see cref="Emails.SendAsync"/>, <see cref="Emails.SendBatchAsync(IEnumerable{SendMailRequest}, IdempotentRequestOptions?, CancellationToken)"/> and the builder.
/// </summary>
public static partial class MessageValidation
{
    /// <summary>The maximum number of name/value tags (one less with a legacy tag).</summary>
    public const int MaxTags = 20;

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagName();

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagValue();

    /// <summary>
    /// Validates name/value tags: at most 20 (19 with a legacy tag), names match
    /// <c>^[A-Za-z0-9_-]{1,32}$</c>, do not start with <c>__lettermint</c> and are unique,
    /// values match <c>^[A-Za-z0-9_-]{1,64}$</c>.
    /// </summary>
    /// <exception cref="LettermintValidationException">A tag is invalid.</exception>
    public static void ValidateTags(IReadOnlyList<MessageTagInput>? tags, bool hasLegacyTag, string field = "tags")
    {
        if (tags is null)
        {
            return;
        }
        var maximum = hasLegacyTag ? MaxTags - 1 : MaxTags;
        if (tags.Count > maximum)
        {
            throw new LettermintValidationException(
                hasLegacyTag ? $"A legacy tag and no more than {maximum} message tags are permitted." : $"No more than {maximum} message tags are permitted.",
                field);
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (tag is null || tag.Name is null || tag.Value is null)
            {
                throw new LettermintValidationException("Message tags must have a name and a value.", field);
            }
            if (!TagName().IsMatch(tag.Name))
            {
                throw new LettermintValidationException("Message tag names must match ^[A-Za-z0-9_-]{1,32}$.", field);
            }
            if (tag.Name.StartsWith("__lettermint", StringComparison.OrdinalIgnoreCase))
            {
                throw new LettermintValidationException("Message tag names must not start with __lettermint.", field);
            }
            if (!TagValue().IsMatch(tag.Value))
            {
                throw new LettermintValidationException("Message tag values must match ^[A-Za-z0-9_-]{1,64}$.", field);
            }
            if (!names.Add(tag.Name))
            {
                throw new LettermintValidationException("Message tag names must be unique (case-sensitive).", field);
            }
        }
    }

    internal static void Validate(SendMailRequest message, string prefix)
    {
        string At(string field) => prefix.Length == 0 ? field : $"{prefix}.{field}";
        if (message is null)
        {
            throw new LettermintValidationException("An email message must not be null.", prefix.Length == 0 ? "message" : prefix);
        }
        ValidateTags(message.Tags, message.Tag.IsSet && !string.IsNullOrEmpty(message.Tag.Value), At("tags"));
        if (message.Attachments is { } attachments)
        {
            for (var index = 0; index < attachments.Count; index++)
            {
                var attachment = attachments[index];
                if (attachment is null || string.IsNullOrEmpty(attachment.Filename))
                {
                    throw new LettermintValidationException("An attachment needs a filename.", $"{At("attachments")}[{index}]");
                }
                if (attachment.Content is null)
                {
                    throw new LettermintValidationException("Attachment content must be a base64 string.", $"{At("attachments")}[{index}]");
                }
            }
        }
    }
}
