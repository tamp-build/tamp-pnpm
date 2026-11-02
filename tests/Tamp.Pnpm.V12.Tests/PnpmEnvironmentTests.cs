using Tamp;
using Tamp.Pnpm.V12;
using Xunit;

namespace Tamp.Pnpm.V12.Tests;

public sealed class PnpmEnvironmentTests
{
    private static Tool T() => PnpmTests.FakeTool();
    private static readonly Secret Token = new("npm token", "FAKE-TOKEN-not-real-0001");
    private static readonly Secret Otp = new("otp", "FAKE-OTP-123456");

    private static void NoSecretInArgv(CommandPlan plan, params Secret[] secrets)
    {
        foreach (var s in secrets)
            Assert.DoesNotContain(plan.Arguments, a => a.Contains(s.Reveal(), StringComparison.Ordinal));
    }

    // ---- registry ----

    [Fact]
    public void Registry_Is_Always_Set_Blank_When_Unset()
    {
        var plan = Pnpm.Install(T());
        Assert.Equal("", plan.Environment["PNPM_CONFIG_REGISTRY"]);
    }

    [Fact]
    public void Registry_Is_Passed_Through_Env_Not_Argv()
    {
        var plan = Pnpm.Install(T(), s => s.SetRegistry("https://pkgs.example.test/npm/"));
        Assert.Equal("https://pkgs.example.test/npm/", plan.Environment["PNPM_CONFIG_REGISTRY"]);
        Assert.DoesNotContain(plan.Arguments, a => a.Contains("pkgs.example.test"));
    }

    [Fact]
    public void Registry_Beats_Caller_Environment_Variable()
    {
        var plan = Pnpm.Install(T(), s => s.SetRegistry("https://a.test/").SetEnvironmentVariable("PNPM_CONFIG_REGISTRY", "https://evil.test/"));
        Assert.Equal("https://a.test/", plan.Environment["PNPM_CONFIG_REGISTRY"]);
    }

    [Theory]
    [InlineData("pkgs.example.test")]
    [InlineData("ftp://x.test/")]
    [InlineData("file:///etc")]
    public void Registry_Must_Be_Http_Url(string bad) =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(T(), s => s.SetRegistry(bad)));

    [Fact]
    public void Every_Verb_Pins_The_Registry()
    {
        var tool = T();
        var plans = new[]
        {
            Pnpm.Install(tool), Pnpm.Add(tool, s => s.AddPackage("a")), Pnpm.Remove(tool, s => s.AddPackage("a")),
            Pnpm.Run(tool, s => s.SetScript("b")), Pnpm.Exec(tool, s => s.SetCommand("c")),
            Pnpm.Dlx(tool, s => s.SetPackage("a@1.0.0")), Pnpm.Fetch(tool), Pnpm.Audit(tool), Pnpm.StorePath(tool),
            Pnpm.StorePrune(tool), Pnpm.Pack(tool), Pnpm.Publish(tool), Pnpm.Deploy(tool, s => s.SetDirectory("d")),
            Pnpm.SetVersion(tool, "1.0.0"), Pnpm.Raw(tool, "why", "x"),
        };
        foreach (var p in plans) Assert.Equal("", p.Environment["PNPM_CONFIG_REGISTRY"]);
    }

    // ---- auth token ----

    [Fact]
    public void AuthToken_Default_Registry_Is_Npmjs()
    {
        var plan = Pnpm.Install(T(), s => s.SetAuthToken(Token));
        Assert.Equal(Token.Reveal(), plan.Environment["PNPM_CONFIG_//registry.npmjs.org/:_authToken"]);
        Assert.Contains(Token, plan.Secrets);
        NoSecretInArgv(plan, Token);
    }

    [Theory]
    [InlineData("https://pkgs.example.test/npm/", "PNPM_CONFIG_//pkgs.example.test/npm/:_authToken")]
    [InlineData("https://pkgs.example.test/npm", "PNPM_CONFIG_//pkgs.example.test/npm/:_authToken")]
    [InlineData("https://pkgs.example.test:8443/a/b/", "PNPM_CONFIG_//pkgs.example.test:8443/a/b/:_authToken")]
    [InlineData("https://pkgs.example.test/", "PNPM_CONFIG_//pkgs.example.test/:_authToken")]
    [InlineData("http://localhost:4873/", "PNPM_CONFIG_//localhost:4873/:_authToken")]
    public void AuthToken_Key_Follows_Registry(string registry, string expectedKey)
    {
        var plan = Pnpm.Install(T(), s => s.SetRegistry(registry).SetAuthToken(Token));
        Assert.Equal(Token.Reveal(), plan.Environment[expectedKey]);
        Assert.Single(plan.Environment, kv => kv.Key.EndsWith(":_authToken", StringComparison.Ordinal));
    }

    [Fact]
    public void AuthTokenRegistry_Overrides_Key()
    {
        var plan = Pnpm.Install(T(), s => s.SetRegistry("https://a.test/").SetAuthTokenRegistry("https://b.test/feed/").SetAuthToken(Token));
        Assert.True(plan.Environment.ContainsKey("PNPM_CONFIG_//b.test/feed/:_authToken"));
        Assert.False(plan.Environment.ContainsKey("PNPM_CONFIG_//a.test/:_authToken"));
    }

    [Fact]
    public void AuthToken_Refused_For_Plain_Http_Remote_Host()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(T(), s => s.SetRegistry("http://pkgs.example.test/").SetAuthToken(Token)));
        var ok = Pnpm.Install(T(), s => s.SetRegistry("http://pkgs.example.test/").SetAuthToken(Token).SetAllowInsecureAuth());
        Assert.Contains("PNPM_CONFIG_//pkgs.example.test/:_authToken", ok.Environment.Keys);
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(T(), s => s.SetAuthTokenRegistry("http://x.test/").SetAuthToken(Token)));
    }

    [Fact]
    public void No_Token_No_Auth_Key_And_No_Secrets()
    {
        var plan = Pnpm.Install(T());
        Assert.DoesNotContain(plan.Environment.Keys, k => k.EndsWith(":_authToken", StringComparison.Ordinal));
        Assert.Empty(plan.Secrets);
    }

    [Fact]
    public void AuthToken_On_Every_Verb_Is_Env_Only()
    {
        var tool = T();
        var plans = new[]
        {
            Pnpm.Install(tool, s => s.SetAuthToken(Token)), Pnpm.Audit(tool, s => s.SetAuthToken(Token)),
            Pnpm.Publish(tool, s => s.SetAuthToken(Token)), Pnpm.Fetch(tool, s => s.SetAuthToken(Token)),
            Pnpm.Dlx(tool, s => s.SetPackage("a@1.0.0").SetAuthToken(Token)), Pnpm.Raw(tool, "view", "x"),
        };
        foreach (var p in plans.Take(5))
        {
            NoSecretInArgv(p, Token);
            Assert.Contains(Token, p.Secrets);
            Assert.Equal(Token.Reveal(), p.Environment["PNPM_CONFIG_//registry.npmjs.org/:_authToken"]);
        }
    }

    // ---- OTP ----

    [Fact]
    public void Otp_Goes_In_Env_Not_Argv_And_Is_A_Secret()
    {
        var plan = Pnpm.Publish(T(), s => s.SetOtp(Otp).SetAuthToken(Token));
        Assert.Equal(Otp.Reveal(), plan.Environment["PNPM_CONFIG_OTP"]);
        Assert.DoesNotContain("--otp", plan.Arguments);
        NoSecretInArgv(plan, Otp, Token);
        Assert.Contains(Otp, plan.Secrets);
        Assert.Contains(Token, plan.Secrets);
    }

    [Fact]
    public void Publish_Without_Otp_Blanks_Inherited_Otp()
    {
        var plan = Pnpm.Publish(T());
        Assert.Equal("", plan.Environment["PNPM_CONFIG_OTP"]);
        Assert.Empty(plan.Secrets);
    }

    [Fact]
    public void Otp_Only_Set_For_Publish() =>
        Assert.DoesNotContain("PNPM_CONFIG_OTP", Pnpm.Install(T()).Environment.Keys);

    // ---- CI and other config ----

    [Fact]
    public void CI_Is_Not_Touched_Because_Flags_Are_Explicit()
    {
        var plan = Pnpm.Install(T());
        Assert.DoesNotContain("CI", plan.Environment.Keys);
        Assert.Contains("--frozen-lockfile", plan.Arguments);
    }

    [Fact]
    public void Caller_Environment_Is_Preserved()
    {
        var plan = Pnpm.Install(T(), s => s.SetEnvironmentVariable("HTTPS_PROXY", "http://proxy.test:3128").SetEnvironmentVariable("CI", "true"));
        Assert.Equal("http://proxy.test:3128", plan.Environment["HTTPS_PROXY"]);
        Assert.Equal("true", plan.Environment["CI"]);
    }

    [Fact]
    public void Environment_Hardening_Applies_To_Raw_Too()
    {
        var plan = Pnpm.Raw(T(), "why", "x");
        Assert.Equal("", plan.Environment["PNPM_CONFIG_REGISTRY"]);
    }

    [Fact]
    public void Secret_Object_Never_Reveals_Itself_In_ToString()
    {
        Assert.DoesNotContain(Token.Reveal(), Token.ToString());
        Assert.DoesNotContain(Token.Reveal(), Pnpm.Install(T(), s => s.SetAuthToken(Token)).ToString());
    }
}
