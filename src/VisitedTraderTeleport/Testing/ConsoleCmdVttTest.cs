#if VTT_TEST_HARNESS
using System.Collections.Generic;

namespace VisitedTraderTeleport;

// Confirmed against Assembly-CSharp.dll (2026-07-24, 7DTD 3.0.1 b4): ConsoleCmdAbstract
// requires getCommands()/getDescription()/Execute() to be overridden; getHelp() is optional.
// Same shape as the game's own built-in commands (e.g. ConsoleCmdPIRS).
internal sealed class ConsoleCmdVttTest : ConsoleCmdAbstract
{
    public override string[] getCommands()
    {
        return new[] { "vtttest" };
    }

    public override string getDescription()
    {
        return "VisitedTraderTeleport headless test harness (test builds only, requires EnableTestHarness.txt next to the mod DLL).";
    }

    public override string getHelp()
    {
        return "vtttest <record <traderEntityId>|teleport <destinationKey>|list|" +
               "dialog <open <traderEntityId>|seed <count>|dump|select <responseId>|close>>";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        if (!VttTestHarnessGate.IsEnabled())
        {
            SdtdConsole.Instance.Output("[vtttest] disabled: create EnableTestHarness.txt next to the mod DLL to opt in.");
            return;
        }

        VttTestHarness.Execute(_params, _senderInfo);
    }
}
#endif
