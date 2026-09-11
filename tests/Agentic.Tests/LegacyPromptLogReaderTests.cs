namespace Agentic.Tests;

// Historical-format coverage is intentionally isolated for removal with LegacyPromptLogReader.
public sealed class LegacyPromptLogReaderTests
{
    [Theory]
    [InlineData("prompt-log:\n\"entry\"\nprompt-log-end:\n", "entry")]
    [InlineData("prompt-log:\n\"entry\"\n\nprompt-log-end:\n", "entry")]
    [InlineData("prompt-log:\n\"first\"\n\"\"\n\"last\"\n\"\"\n\n\"next\"\nprompt-log-end:\n", "first\n\nlast\n\n\nnext")]
    [InlineData("prompt-log:\n\"prompt-log:\"\n\"prompt-log-end:\"\n\"prompt-log-format: raw-v1\"\nprompt-log-end:\n", "prompt-log:\nprompt-log-end:\nprompt-log-format: raw-v1")]
    [InlineData("prompt-log:\n\"quotes \\\" \\\\ Unicode \\u6f22 [red]literal[/]\"\nprompt-log-end:\n", "quotes \" \\ Unicode 漢 [red]literal[/]")]
    public void HistoricalJsonPreservesDecodedText(string block, string expected)
    {
        var log = PromptLogReader.Parse(block, blockOnly: true);
        Assert.NotNull(log);
        Assert.Equal("legacy prompt log (JSON)", log.Description);
        Assert.Equal(expected, log.Text);
    }

    [Fact]
    public void StandaloneNumberedLogRemainsRaw()
    {
        const string text = "1. \"first\"\n2. Q: \"question\" -> A: \"answer\"\ncontinued";
        var log = PromptLogReader.Parse("prompt-log:\n\n" + text + "\n");
        Assert.NotNull(log);
        Assert.Equal("legacy prompt log (raw text)", log.Description);
        Assert.Equal(text, log.Text);
    }

    [Theory]
    [InlineData("prompt-log:\n123\nprompt-log-end:\n")]
    [InlineData("prompt-log:\nnull\nprompt-log-end:\n")]
    [InlineData("prompt-log:\ntrue\nprompt-log-end:\n")]
    [InlineData("prompt-log:\n{}\nprompt-log-end:\n")]
    [InlineData("prompt-log:\n\"unterminated\nprompt-log-end:\n")]
    [InlineData("prompt-log:\n\"entry\"\n")]
    [InlineData("outside\nprompt-log:\n\"entry\"\nprompt-log-end:\n")]
    [InlineData("prompt-log:\n\"entry\"\nprompt-log-end:\nprompt-log-end:\n")]
    [InlineData("prompt-log:\n\n\"entry\"\nprompt-log-end:\n")]
    [InlineData("prompt-log:\n\"entry\"\n\n\n\"next\"\nprompt-log-end:\n")]
    [InlineData("prompt-log:\nprompt-log-end:\n")]
    public void MalformedHistoricalBlocksRemainErrors(string block)
        => _ = Assert.Throws<FormatException>(() => PromptLogReader.Parse(block, blockOnly: true));
}
