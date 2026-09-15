using Lettermint.Models;

namespace Lettermint;

public static class LettermintClient
{
    public static EmailClient Email(string token, ClientOptions? options = null) => new(new Transport(token, true, options));
    public static ApiClient Api(string token, ClientOptions? options = null) => new(new Transport(token, false, options));
}

public sealed partial class EmailClient
{
    public Task<RescheduleMessageResponse> RescheduleAsync(string messageId, RescheduleMessageRequest payload, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.SendAsync<RescheduleMessageResponse>(HttpMethod.Patch, $"/messages/{Transport.Segment(messageId)}", payload, options, cancellationToken);
    public Task<CancelScheduledMessageResponse> CancelAsync(string messageId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.SendAsync<CancelScheduledMessageResponse>(HttpMethod.Post, $"/messages/{Transport.Segment(messageId)}/cancel", null, options, cancellationToken);
    public EmailBuilder Compose() => new(this);
    public EmailBuilder From(string from) => Compose().From(from);

    // Use the same email model for single and batch sends.
    public Task<List<SendBatchEmailResponseItem>> SendBatchAsync(IEnumerable<SendMailRequest> payload, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.SendAsync<List<SendBatchEmailResponseItem>>(HttpMethod.Post, "/send/batch", payload, options, cancellationToken);
}

public sealed class EmailBuilder
{
    private readonly EmailClient client;
    private readonly SendMailRequest payload = new();
    private string? idempotencyKey;
    internal EmailBuilder(EmailClient client) => this.client = client;
    public EmailBuilder From(string value) { payload.From = value; return this; }
    public EmailBuilder To(params string[] values) { payload.To = [.. values]; return this; }
    public EmailBuilder Cc(params string[] values) { payload.Cc = [.. values]; return this; }
    public EmailBuilder Bcc(params string[] values) { payload.Bcc = [.. values]; return this; }
    public EmailBuilder ReplyTo(params string[] values) { payload.ReplyTo = [.. values]; return this; }
    public EmailBuilder Subject(string value) { payload.Subject = value; return this; }
    public EmailBuilder Html(string? value) { payload.Html = value; return this; }
    public EmailBuilder Text(string? value) { payload.Text = value; return this; }
    public EmailBuilder Route(string? value) { payload.Route = value; return this; }
    public EmailBuilder ScheduledAt(string value) { payload.ScheduledAt = value; return this; }
    public EmailBuilder Tags(params MessageTag[] values)
    {
        ValidateTags(values.Select(value => (value.Name, value.Value)));
        payload.Tags = [.. values.Select(value => new SendMailRequestTagsItem { Name = value.Name, Value = value.Value })];
        return this;
    }
    public EmailBuilder Tags(params SendMailRequestTagsItem[] values)
    {
        ValidateTags(values.Select(value => (value.Name ?? "", value.Value ?? "")));
        payload.Tags = [.. values];
        return this;
    }
    public EmailBuilder Tag(string? value)
    {
        if (value is not null && payload.Tags?.Count >= 20) throw new ArgumentException("A legacy tag and no more than 19 message tags are permitted", nameof(value));
        payload.Tag = value;
        return this;
    }
    public EmailBuilder Headers(IReadOnlyDictionary<string, string> values) { payload.Headers = new(values); return this; }
    public EmailBuilder Metadata(IReadOnlyDictionary<string, string> values) { payload.Metadata = new(values); return this; }
    public EmailBuilder Settings(SendMailRequestSettings settings) { payload.Settings = settings; return this; }
    public EmailBuilder IdempotencyKey(string value) { idempotencyKey = value; return this; }
    public EmailBuilder Attach(string filename, string content, string? contentId = null, string? contentType = null)
    {
        (payload.Attachments ??= []).Add(new() { Filename = filename, Content = content, ContentId = contentId, ContentType = contentType });
        return this;
    }
    public Task<SendEmailResponse> SendAsync(CancellationToken cancellationToken = default)
        => client.SendAsync(payload, new RequestOptions { IdempotencyKey = idempotencyKey }, cancellationToken);

    private void ValidateTags(IEnumerable<(string Name, string Value)> values)
    {
        var tags = values.ToList();
        var maximum = payload.Tag is null ? 20 : 19;
        if (tags.Count > maximum) throw new ArgumentException($"No more than {maximum} message tags are permitted", nameof(values));
        foreach (var tag in tags) _ = new MessageTag(tag.Name, tag.Value);
        if (tags.Select(tag => tag.Name).Distinct(StringComparer.Ordinal).Count() != tags.Count)
            throw new ArgumentException("Message tag names must be unique and case-sensitive", nameof(values));
    }
}
