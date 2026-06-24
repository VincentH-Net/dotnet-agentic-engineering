namespace Agentic.Check;

static class ToolHeader
{
    public const string RepositoryUrl = "https://github.com/VincentH-Net/dotnet-agentic-engineering";
    public const string AuthorProfileUrl = "https://github.com/VincentH-Net";

    public const string Art = """
             .----------------.
            /  repo scanner  /|
           /_____[ OK ]_____/ |
           |  directives   |  |
           |  skills       |  |
           |_______________| /
           '---------------'

     _                    _   _        ____ _               _
    / \   __ _  ___ _ __ | |_(_) ___  / ___| |__   ___  ___| | __
   / _ \ / _` |/ _ \ '_ \| __| |/ __|| |   | '_ \ / _ \/ __| |/ /
  / ___ \ (_| |  __/ | | | |_| | (__ | |___| | | |  __/ (__|   <
 /_/   \_\__, |\___|_| |_|\__|_|\___| \____|_| |_|\___|\___|_|\_\
         |___/
 """;

    public static string Description =>
        $"""
        Checks a repo for agentic directives and skills, then installs or updates selected recommendations.
        Tool repo: {RepositoryUrl}
        Author:    {AuthorProfileUrl}
        """;
}
