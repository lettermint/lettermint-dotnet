using System.Text;
using System.Text.Json.Nodes;
using Lettermint.Models;

namespace Lettermint.Tests;

public class EmailTests
{
    [Fact]
    public async Task SendsAMessageAsJsonWithTheSendingToken()
    {
        var (client, handler) = Fake.Client();
        var result = await client.Emails.SendAsync(new SendMailRequest
        {
            From = "Acme <hello@acme.test>",
            To = ["jane@example.test"],
            ReplyTo = ["support@acme.test"],
            Subject = "Your order",
            Html = "<p>Shipped</p>",
            Metadata = new Dictionary<string, string> { ["order_id"] = "1234" },
            Tags = [new MessageTagInput { Name = "campaign", Value = "orders" }],
        });
        Assert.Equal("msg_1", result.MessageId);
        Assert.Equal(MessageStatus.Pending, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.lettermint.co/v1/send", request.Url.ToString());
        Assert.Equal(Fake.SendingToken, request.Header("x-lettermint-token"));
        Assert.Null(request.Header("Authorization"));
        Assert.Equal("application/json", request.Header("Content-Type"));
        Assert.Null(request.Header("Idempotency-Key"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            {"from":"Acme <hello@acme.test>","to":["jane@example.test"],"reply_to":["support@acme.test"],"subject":"Your order",
             "metadata":{"order_id":"1234"},"html":"<p>Shipped</p>","tags":[{"name":"campaign","value":"orders"}]}
            """), request.Json), request.Body);
    }

    [Fact]
    public async Task TheIdempotencyKeyIsAPerCallOptionOnly()
    {
        var (client, handler) = Fake.Client();
        await client.Emails.SendAsync(Fake.Message("A"), new IdempotentRequestOptions { IdempotencyKey = "key-a" });
        await client.Emails.SendAsync(Fake.Message("B"));
        var builder = client.Emails.Compose(Fake.Message("C"));
        await builder.SendAsync(new IdempotentRequestOptions { IdempotencyKey = "key-c" });
        await builder.SendAsync();
        await client.Emails.SendBatchAsync([Fake.Message("D")], new IdempotentRequestOptions { IdempotencyKey = "batch" });
        Assert.Equal(["key-a", null, "key-c", null, "batch"], handler.Requests.Select(r => r.Header("Idempotency-Key")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    public async Task RejectsInvalidIdempotencyKeys(string key)
    {
        var (client, handler) = Fake.Client();
        var error = await Assert.ThrowsAsync<LettermintValidationException>(() => client.Emails.SendAsync(Fake.Message(), new IdempotentRequestOptions { IdempotencyKey = key }));
        Assert.Equal("IdempotencyKey", error.Field);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void TheBuilderIsImmutable()
    {
        var (client, _) = Fake.Client();
        var empty = client.Emails.Compose();
        var welcome = empty.From("hello@acme.test").Subject("Welcome").Tags(new MessageTagInput { Name = "campaign", Value = "welcome" });
        var jane = welcome.To("jane@example.test").Html("<p>Hi Jane</p>");
        var john = welcome.To("john@example.test").Html("<p>Hi John</p>").Cc("boss@example.test");

        Assert.Equal(string.Empty, empty.Build().From);
        Assert.Empty(welcome.Build().To);
        Assert.False(welcome.Build().Html.IsSet);
        Assert.Equal(["jane@example.test"], jane.Build().To);
        Assert.Null(jane.Build().Cc);
        Assert.Equal(["john@example.test"], john.Build().To);
        Assert.Equal("<p>Hi John</p>", john.Build().Html.Value);
        Assert.NotSame(welcome, jane);
    }

    [Fact]
    public async Task ABaseBuilderCanBeSentConcurrently()
    {
        var (client, handler) = Fake.Client(async (_, _, cancellationToken) =>
        {
            await Task.Delay(10, cancellationToken);
            return Fake.Json(202, Fake.Sent);
        });
        var welcome = client.Emails.Compose().From("hello@acme.test").Subject("Welcome");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
        {
            await Task.Yield();
            var email = welcome.To($"user{index}@example.test");
            await Task.Yield();
            await email.Text($"Hello {index}").SendAsync(new IdempotentRequestOptions { IdempotencyKey = $"key-{index}" });
        }));
        Assert.Equal(20, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            var index = request.Header("Idempotency-Key")!["key-".Length..];
            Assert.Equal($"user{index}@example.test", request.Json!["to"]![0]!.GetValue<string>());
            Assert.Equal($"Hello {index}", request.Json!["text"]!.GetValue<string>());
        }
    }

    [Fact]
    public void AThrowingSetterLeavesTheBuilderUnchanged()
    {
        var (client, _) = Fake.Client();
        var builder = client.Emails.Compose().From("hello@acme.test").Tags(new MessageTagInput { Name = "ok", Value = "1" });
        var error = Assert.Throws<LettermintValidationException>(() => builder.Tags(new MessageTagInput { Name = "not a valid tag name!", Value = "x" }));
        Assert.Equal("tags", error.Field);
        Assert.Equal("ok", Assert.Single(builder.Build().Tags!).Name);
    }

    [Theory]
    [InlineData("not valid", "x", "Message tag names must match ^[A-Za-z0-9_-]{1,32}$.")]
    [InlineData("abcdefghijabcdefghijabcdefghijabc", "x", "Message tag names must match ^[A-Za-z0-9_-]{1,32}$.")]
    [InlineData("__lettermint_x", "x", "Message tag names must not start with __lettermint.")]
    [InlineData("__LetterMint", "x", "Message tag names must not start with __lettermint.")]
    [InlineData("ok", "", "Message tag values must match ^[A-Za-z0-9_-]{1,64}$.")]
    [InlineData("ok", "with space", "Message tag values must match ^[A-Za-z0-9_-]{1,64}$.")]
    public void ValidatesTagNamesAndValues(string name, string value, string message)
    {
        var error = Assert.Throws<LettermintValidationException>(() => MessageValidation.ValidateTags([new MessageTagInput { Name = name, Value = value }], false));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void ValidatesTagCountsAndUniqueness()
    {
        MessageTagInput Tag(int i) => new() { Name = $"t{i}", Value = "v" };
        MessageValidation.ValidateTags(Enumerable.Range(0, 20).Select(Tag).ToList(), false);
        MessageValidation.ValidateTags(Enumerable.Range(0, 19).Select(Tag).ToList(), true);
        Assert.Equal("No more than 20 message tags are permitted.", Assert.Throws<LettermintValidationException>(() => MessageValidation.ValidateTags(Enumerable.Range(0, 21).Select(Tag).ToList(), false)).Message);
        Assert.Equal("A legacy tag and no more than 19 message tags are permitted.", Assert.Throws<LettermintValidationException>(() => MessageValidation.ValidateTags(Enumerable.Range(0, 20).Select(Tag).ToList(), true)).Message);
        Assert.Equal("Message tag names must be unique (case-sensitive).", Assert.Throws<LettermintValidationException>(() => MessageValidation.ValidateTags([Tag(1), Tag(1)], false)).Message);
        MessageValidation.ValidateTags([new() { Name = "Tag", Value = "a" }, new() { Name = "tag", Value = "b" }], false);

        var (client, _) = Fake.Client();
        var twenty = client.Emails.Compose().Tags(Enumerable.Range(0, 20).Select(Tag));
        Assert.Throws<LettermintValidationException>(() => twenty.Tag("legacy"));
        Assert.Equal("legacy", twenty.Tags(Enumerable.Range(0, 19).Select(Tag)).Tag("legacy").Build().Tag.Value);
    }

    [Fact]
    public async Task ValidatesBeforeAnyRequestWithTheFieldOfTheMessage()
    {
        var (client, handler) = Fake.Client();
        var invalid = Fake.Message() with { Tags = [new MessageTagInput { Name = "bad name", Value = "x" }] };
        Assert.Equal("tags", (await Assert.ThrowsAsync<LettermintValidationException>(() => client.Emails.SendAsync(invalid))).Field);
        var batch = await Assert.ThrowsAsync<LettermintValidationException>(() => client.Emails.SendBatchAsync([Fake.Message(), invalid]));
        Assert.Equal("messages[1].tags", batch.Field);
        var attachment = Fake.Message() with { Attachments = [new MessageAttachmentInput { Filename = "", Content = "eA==" }] };
        Assert.Equal("attachments[0]", (await Assert.ThrowsAsync<LettermintValidationException>(() => client.Emails.SendAsync(attachment))).Field);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EncodesAttachmentsFromBytesAndKeepsBase64()
    {
        var (client, handler) = Fake.Client();
        await client.Emails.Compose(Fake.Message())
            .Attach("invoice.pdf", Encoding.UTF8.GetBytes("%PDF"), "application/pdf")
            .Attach(EmailAttachment.FromBase64("logo.png", "aGVsbG8="))
            .Attach(new EmailAttachment("inline.png", [1, 2, 3]) { ContentId = "logo" })
            .SendAsync();
        var attachments = handler.Requests[0].Json!["attachments"]!.AsArray();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            [{"filename":"invoice.pdf","content":"JVBERg==","content_type":"application/pdf"},
             {"filename":"logo.png","content":"aGVsbG8="},
             {"filename":"inline.png","content":"AQID","content_id":"logo"}]
            """), attachments), attachments.ToJsonString());
        Assert.Throws<LettermintValidationException>(() => new EmailAttachment("", [1]));
    }

    [Fact]
    public async Task NullRemovesOptionalFields()
    {
        var (client, handler) = Fake.Client();
        var builder = client.Emails.Compose(Fake.Message()).Html("<p>x</p>").Tag("legacy").ScheduledAt("tomorrow 9am").Route("broadcast");
        await builder.Html(null).Text(null).Tag(null).ScheduledAt(null).Route(null).SendAsync();
        var body = handler.Requests[0].Json!.AsObject();
        Assert.Equal(["from", "subject", "to"], body.Select(p => p.Key).Order());
    }

    [Fact]
    public void FormatsScheduledInstantsAsIso8601Utc()
    {
        var (client, _) = Fake.Client();
        var builder = client.Emails.Compose().ScheduledAt(new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.FromHours(2)));
        Assert.Equal("2026-10-20T09:00:00.000Z", builder.Build().ScheduledAt);
    }

    [Fact]
    public async Task SetsEveryFieldThroughTheBuilder()
    {
        var (client, handler) = Fake.Client();
        await client.Emails.Compose()
            .From("a@example.test").To("b@example.test", "c@example.test").Cc("d@example.test").Bcc("e@example.test").ReplyTo("f@example.test")
            .Subject("S").Html("<p>H</p>").Text("T")
            .Headers(new Dictionary<string, string> { ["X-Custom"] = "1" })
            .Metadata(new Dictionary<string, string> { ["k"] = "v" })
            .Tag("legacy").Tags(new MessageTagInput { Name = "n", Value = "v" })
            .Route("transactional").ScheduledAt("2026-10-20T09:00:00Z")
            .Settings(new SendMailRequestSettings { TrackOpens = true, Tls = TlsPolicy.Enforced })
            .SandboxResult(Models.SandboxResult.HardBounced)
            .SendAsync();
        var body = handler.Requests[0].Json!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            {"route":"transactional","from":"a@example.test","to":["b@example.test","c@example.test"],"cc":["d@example.test"],"bcc":["e@example.test"],
             "reply_to":["f@example.test"],"subject":"S","scheduled_at":"2026-10-20T09:00:00Z","sandbox_result":"hard_bounced",
             "headers":{"X-Custom":"1"},"metadata":{"k":"v"},"tag":"legacy","tags":[{"name":"n","value":"v"}],
             "settings":{"track_opens":true,"tls":"enforced"},"html":"<p>H</p>","text":"T"}
            """), body), body.ToJsonString());
    }

    [Fact]
    public async Task SendsBatchesOfMessagesAndBuilders()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(202, new[] { Fake.Sent, new { message_id = "msg_2", status = "scheduled", scheduled_at = "2026-10-20T09:00:00Z" } }));
        var results = await client.Emails.SendBatchAsync([Fake.Message("A"), Fake.Message("B")]);
        Assert.Equal(2, results.Count);
        Assert.Equal("2026-10-20T09:00:00Z", results[1].ScheduledAt);
        var request = handler.Requests[0];
        Assert.Equal("https://api.lettermint.co/v1/send/batch", request.Url.ToString());
        Assert.Equal(["A", "B"], request.Json!.AsArray().Select(m => m!["subject"]!.GetValue<string>()));

        var builder = client.Emails.Compose().From("a@example.test").To("b@example.test").Subject("C");
        await client.Emails.SendBatchAsync([builder, builder.Subject("D")]);
        Assert.Equal(["C", "D"], handler.Requests[1].Json!.AsArray().Select(m => m!["subject"]!.GetValue<string>()));
    }

    [Fact]
    public void ComposeFromAMessageCopiesIt()
    {
        var (client, _) = Fake.Client();
        var message = Fake.Message("Template");
        var builder = client.Emails.Compose(message).Subject("Changed");
        Assert.Equal("Template", message.Subject);
        Assert.Equal("Changed", builder.Build().Subject);
        Assert.Throws<LettermintValidationException>(() => client.Emails.Compose(message with { Tags = [new() { Name = "x y", Value = "z" }] }));
    }

    [Fact]
    public async Task TheClientHoldsNoMessageStateAfterAFailedRequest()
    {
        var (client, handler) = Fake.Client((_, index) => index == 0 ? Fake.Json(500, new { message = "Server Error" }) : Fake.Json(202, Fake.Sent));
        var failed = client.Emails.Compose(Fake.Message("A")).Cc("cc@example.test");
        await Assert.ThrowsAsync<ServerException>(() => failed.SendAsync(new IdempotentRequestOptions { IdempotencyKey = "a" }));
        await client.Emails.Compose().From("c@example.test").To("d@example.test").Subject("C").SendAsync();
        var second = handler.Requests[1];
        Assert.Null(second.Header("Idempotency-Key"));
        Assert.Null(second.Json!["cc"]);
        Assert.Equal("C", second.Json!["subject"]!.GetValue<string>());
    }
}
