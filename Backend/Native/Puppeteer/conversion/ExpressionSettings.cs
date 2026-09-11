namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal sealed class ExpressionSettings
{
    public bool handEnabled { get; set; }
    public bool menuEnabled { get; set; }
    public List<ExpressionTarget> targets { get; set; } = new();
    public List<HandExpressionRule> handRules { get; set; } = new();
    public List<MenuExpressionEntry> menuEntries { get; set; } = new();
}

internal sealed class ExpressionTarget
{
    public ulong rendererId { get; set; }
    public string blendShape { get; set; } = "";
    public float baseline { get; set; }
}

internal sealed class ExpressionValue
{
    public int target { get; set; }
    public float value { get; set; }
}

internal sealed class HandExpressionRule
{
    public string left { get; set; } = "Any";
    public string right { get; set; } = "Any";
    public List<ExpressionValue> values { get; set; } = new();
}

internal sealed class MenuExpressionEntry
{
    public string name { get; set; } = "";
    public List<ExpressionValue> values { get; set; } = new();
}
