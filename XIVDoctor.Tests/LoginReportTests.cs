using XIVDoctor;
using Xunit;

public class LoginReportTests
{
    private static readonly Report Fine = new(true, true, "overlays ready on port 10501");
    private static readonly Report Parser = new(true, true, "scan thread fine");

    [Fact]
    public void Prints_after_the_zone_lines_settled_and_both_answered()
    {
        Assert.True(LoginReport.ReadyToPrint(sinceLogin: 12, sinceZone: 6, sinceChat: 10, bothHealthy: true));
    }

    [Fact]
    public void Holds_while_the_zone_lines_are_still_arriving()
    {
        Assert.False(LoginReport.ReadyToPrint(sinceLogin: 12, sinceZone: 2, sinceChat: 10, bothHealthy: true));
        Assert.False(LoginReport.ReadyToPrint(sinceLogin: 12, sinceZone: null, sinceChat: 10, bothHealthy: true));
    }

    [Fact]
    public void Holds_while_chat_is_still_flowing_but_not_past_the_cap()
    {
        Assert.False(LoginReport.ReadyToPrint(sinceLogin: 12, sinceZone: 8, sinceChat: 1, bothHealthy: true));
        Assert.True(LoginReport.ReadyToPrint(sinceLogin: 12, sinceZone: 8, sinceChat: 6, bothHealthy: true));
        Assert.True(LoginReport.ReadyToPrint(sinceLogin: 12, sinceZone: 8, sinceChat: null, bothHealthy: true));
        Assert.True(LoginReport.ReadyToPrint(sinceLogin: 46, sinceZone: 8, sinceChat: 1, bothHealthy: true));
    }

    [Fact]
    public void Holds_for_a_slow_layer_but_not_forever()
    {
        Assert.False(LoginReport.ReadyToPrint(sinceLogin: 30, sinceZone: 10, sinceChat: 10, bothHealthy: false));
        Assert.True(LoginReport.ReadyToPrint(sinceLogin: 91, sinceZone: 10, sinceChat: 10, bothHealthy: false));
        Assert.True(LoginReport.ReadyToPrint(sinceLogin: 46, sinceZone: null, sinceChat: 10, bothHealthy: true));
    }

    [Fact]
    public void All_good_names_the_port()
    {
        Assert.Equal("XIV Doctor: all good; parser healthy, overlays ready on port 10501.", LoginReport.Line(Parser, Fine, new List<string>()));
    }

    [Fact]
    public void Trouble_names_the_layer_and_the_fix()
    {
        var line = LoginReport.Line(new Report(true, false, "stalled"), Fine, new List<string>());
        Assert.StartsWith("XIV Doctor: attention.", line);
        Assert.Contains("IINACT unwell", line);
        Assert.Contains("/doctor fix", line);
        Assert.Contains("IINACT not loaded", LoginReport.Line(Report.Absent("not loaded"), Fine, new List<string>()));
    }

    [Fact]
    public void A_layer_that_never_reported_is_said_to_have_run_out_of_time()
    {
        var line = LoginReport.Line(Parser, new Report(true, false, "renderer starting on port 10501"), new List<string>(), waitedOut: true);
        Assert.Contains("still not ready after 90 s", line);
        Assert.Contains("use /doctor fix", line);
    }

    [Fact]
    public void Notes_left_for_the_player_are_appended_even_when_all_is_well()
    {
        var line = LoginReport.Line(Parser, Fine, new List<string> { "IINACT: upstream sync needs a hand (run 42 failed)" });
        Assert.StartsWith("XIV Doctor: attention.", line);
        Assert.Contains("run 42 failed", line);
        Assert.DoesNotContain("/doctor fix", line);
    }

    [Fact]
    public void A_standing_note_ends_with_the_one_instruction_and_is_repeated_every_half_hour()
    {
        var notes = new List<string> { "GatherBuddyReborn: upstream sync needs a hand (workflow run 12 failure)" };
        var line = LoginReport.Line(Parser, Fine, notes);
        Assert.StartsWith("XIV Doctor: attention. ", line);
        Assert.EndsWith(LoginReport.Instruction, line);
        Assert.Equal("Paste this line to your assistant.", LoginReport.DefaultInstruction);
        Assert.DoesNotContain(LoginReport.Instruction, LoginReport.Line(Parser, Fine, new List<string>()));
        Assert.Equal("XIV Doctor: attention, still open. GatherBuddyReborn: upstream sync needs a hand (workflow run 12 failure). " + LoginReport.Instruction,
                     LoginReport.Reminder(notes));
        var t0 = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(LoginReport.ReminderDue(null, t0, anyNotes: true));
        Assert.False(LoginReport.ReminderDue(t0, t0.AddMinutes(29), anyNotes: true));
        Assert.True(LoginReport.ReminderDue(t0, t0.AddMinutes(30), anyNotes: true));
        Assert.False(LoginReport.ReminderDue(null, t0, anyNotes: false));
    }
}
