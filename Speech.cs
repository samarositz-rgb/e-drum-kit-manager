using System.Runtime.InteropServices;
using System.Windows.Forms.Automation;

namespace TD12KitManager;

/// <summary>
/// Speaks a message with the running screen reader. JAWS and Narrator read UI Automation notifications;
/// NVDA can ignore them from WinForms windows, so when NVDA is running the text goes straight to it
/// through NV Access's controller client (ThirdParty/NVDA, LGPL), and the notification is skipped so nothing is said twice.
/// </summary>
static class Speech
{
    const string Dll = "nvdaControllerClient.dll";

    [DllImport(Dll)] static extern int nvdaController_testIfRunning();
    [DllImport(Dll, CharSet = CharSet.Unicode)] static extern int nvdaController_speakText(string text);
    [DllImport(Dll)] static extern int nvdaController_cancelSpeech();
    [DllImport(Dll, CharSet = CharSet.Unicode)] static extern int nvdaController_brailleMessage(string message);

    static bool dllMissing;

    static bool NvdaRunning()
    {
        if (dllMissing) return false;
        try { return nvdaController_testIfRunning() == 0; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            dllMissing = true;
            return false;
        }
    }

    /// <summary>For the --speech-check diagnostic.</summary>
    public static string Check() => $"NVDA running: {NvdaRunning()}, controller client {(dllMissing ? "missing" : "loaded")}";

    /// <param name="source">Control the message is about (UI Automation needs one).</param>
    /// <param name="interrupt">Stop what's being said first, for quick repeats like arrowing through numbers.</param>
    public static void Say(Control source, string text, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (NvdaRunning())
        {
            if (interrupt) nvdaController_cancelSpeech();
            nvdaController_speakText(text);
            nvdaController_brailleMessage(text);
            return;
        }
        source.AccessibilityObject.RaiseAutomationNotification(
            interrupt ? AutomationNotificationKind.Other : AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent, text);
    }
}
