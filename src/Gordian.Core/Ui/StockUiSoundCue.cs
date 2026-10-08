// src/Gordian.Core/Ui/StockUiSoundCue.cs
namespace Gordian.Core.Ui
{
    /// <summary>
    /// The system sounds of the stock UI, valued by their sound effect id (<c>sound/win/se/se000/se0000NN.spw</c>).
    /// The UI raises them as neutral cues; the audio engine plays them on the System bus.
    /// </summary>
    /// <remarks>
    /// Ids and names referenced from the Windower pol-utils sound list (Apache-2.0, as bundled in xi-tools
    /// <c>src/xi/audio/data/SFXInfo.xml</c>, https://github.com/vekien/xi-tools), "Main Sounds" folder <c>se000</c>.
    /// Which UI action plays which sound is our reading of the names; confirm against retail.
    /// </remarks>
    public enum StockUiSoundCue
    {
        /// <summary>The menu cursor moved ("Menu Movement").</summary>
        CursorMove = 1,

        /// <summary>A menu entry was chosen ("Menu Selection").</summary>
        Select = 2,

        /// <summary>A dialog or prompt was confirmed ("Dialog Confirmation").</summary>
        DialogConfirm = 3,

        /// <summary>The action is not available ("Unavailable Action").</summary>
        Unavailable = 4,

        /// <summary>Level up.</summary>
        LevelUp = 7,

        /// <summary>Quest completed.</summary>
        QuestComplete = 8,

        /// <summary>A target was selected ("Target Selection").</summary>
        TargetSelect = 9,

        /// <summary>The target changed to another ("Target Switch").</summary>
        TargetSwitch = 10,

        /// <summary>The target command menu opened ("Target Menu Open").</summary>
        TargetMenuOpen = 11,

        /// <summary>The main menu turned a page ("Main Menu Page Switch").</summary>
        PageSwitch = 13,

        /// <summary>The main menu opened ("Main Menu Open").</summary>
        MainMenuOpen = 14,

        /// <summary>A menu closed ("Close Menu").</summary>
        Close = 15,

        /// <summary>
        /// "Message Arrival": PlayOnline's message sound (a friend's message through POL), not the tell sound (the
        /// maintainer's in-game check, 2026-10-07). Nothing raises it yet.
        /// </summary>
        MessageArrival = 39,

        /// <summary>
        /// An incoming tell. Provisional, pending a retail listen: <c>se000040</c> is the one unnamed entry of the client's
        /// system sound table (<c>ROM/0/0.DAT</c> <c>/syst/soun/0040</c>, after Message Arrival's <c>0039</c>) of a
        /// notification's length (1.26 s); the other unnamed entries are a 31 s sound (5) and a 40 ms click (6).
        /// </summary>
        TellArrival = 40,
    }
}
