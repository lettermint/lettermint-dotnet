using System.Text.RegularExpressions;

namespace Lettermint.Internal;

internal enum TokenKind
{
    Sending,
    Team,
}

/// <summary>Token classification and validation. Error messages never contain the token.</summary>
internal static partial class Tokens
{
    /// <summary>Team API tokens: <c>ApiToken::TEAM_PREFIX</c> in the Lettermint backend.</summary>
    [GeneratedRegex(@"\Alm_team_[0-9A-Za-z]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TeamToken();

    /// <summary>Project sending tokens (32 or 22 random characters): <c>ApiToken::PROJECT_PREFIX</c>.</summary>
    [GeneratedRegex(@"\Alm_[0-9A-Za-z]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SendingToken();

    /// <summary>
    /// Classifies a token passed as <c>new LettermintClient(token)</c>. The team
    /// pattern is checked first, because every team token also starts with <c>lm_</c>.
    /// </summary>
    public static TokenKind Detect(string? token)
    {
        if (token is not null)
        {
            if (TeamToken().IsMatch(token))
            {
                return TokenKind.Team;
            }
            if (SendingToken().IsMatch(token))
            {
                return TokenKind.Sending;
            }
        }
        throw new LettermintConfigException("Unrecognised token format; pass LettermintOptions.SendingToken or LettermintOptions.TeamToken instead.");
    }

    /// <summary>Validates an explicitly configured token. Returns null when it is not set.</summary>
    public static string? Check(string option, string? token)
    {
        if (token is null)
        {
            return null;
        }
        if (token.Length == 0)
        {
            throw new LettermintConfigException($"{option} must be a non-empty string.");
        }
        foreach (var character in token)
        {
            if (character < '\x21' || character > '\x7e')
            {
                throw new LettermintConfigException($"{option} contains whitespace or characters that are not allowed in an HTTP header.");
            }
        }
        return token;
    }
}
