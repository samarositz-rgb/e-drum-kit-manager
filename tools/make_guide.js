// Builds docs/E-Drum Kit Manager Guide.docx, the full guide with tutorials (Windows and REAPER).
// Run: node tools/make_guide.js
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType, LevelFormat, Table, TableRow, TableCell,
  WidthType, ShadingType, BorderStyle, TableOfContents, PageBreak, Footer, PageNumber, ExternalHyperlink,
} = require("docx");

const FONT = "Arial";
let listInstance = 0;

// "Some **bold** words and a [link](https://...)" -> runs
function runs(text) {
  return text.split(/(\*\*[^*]+\*\*|\[[^\]]+\]\([^)]+\))/).filter(Boolean).map(part => {
    if (part.startsWith("**")) return new TextRun({ text: part.slice(2, -2), bold: true });
    const link = part.match(/^\[([^\]]+)\]\(([^)]+)\)$/);
    if (link) return new ExternalHyperlink({ link: link[2], children: [new TextRun({ text: link[1], style: "Hyperlink" })] });
    return new TextRun(part);
  });
}
const h1 = t => new Paragraph({ heading: HeadingLevel.HEADING_1, pageBreakBefore: true, children: [new TextRun(t)] });
const h1First = t => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(t)] });
const h2 = t => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(t)] });
const h3 = t => new Paragraph({ heading: HeadingLevel.HEADING_3, children: [new TextRun(t)] });
const p = t => new Paragraph({ children: runs(t), spacing: { after: 160 } });
function steps(items) {
  const instance = ++listInstance;
  return items.map(t => new Paragraph({ numbering: { reference: "steps", level: 0, instance }, children: runs(t), spacing: { after: 80 } }));
}
const bullets = items => items.map(t => new Paragraph({ numbering: { reference: "bullets", level: 0 }, children: runs(t), spacing: { after: 80 } }));
const tip = (label, t) => new Paragraph({
  children: [new TextRun({ text: label + " ", bold: true }), ...runs(t)],
  spacing: { before: 120, after: 200 }, indent: { left: 360 },
  border: { left: { style: BorderStyle.SINGLE, size: 18, color: "2E75B6", space: 8 } },
});
const TipP = t => tip("Tip:", t);
const Note = t => tip("Good to know:", t);

const border = { style: BorderStyle.SINGLE, size: 4, color: "999999" };
const borders = { top: border, bottom: border, left: border, right: border };
function table(headers, rows, widths) {
  const total = widths.reduce((a, b) => a + b, 0);
  const cell = (text, i, head) => new TableCell({
    borders, width: { size: widths[i], type: WidthType.DXA },
    margins: { top: 80, bottom: 80, left: 120, right: 120 },
    shading: head ? { fill: "DCE6F1", type: ShadingType.CLEAR, color: "auto" } : undefined,
    children: [new Paragraph({ children: head ? [new TextRun({ text, bold: true })] : runs(text) })],
  });
  return [new Table({
    width: { size: total, type: WidthType.DXA }, columnWidths: widths,
    rows: [new TableRow({ tableHeader: true, children: headers.map((h, i) => cell(h, i, true)) }),
           ...rows.map(r => new TableRow({ children: r.map((c, i) => cell(c, i, false)) }))],
  }), new Paragraph({ children: [], spacing: { after: 120 } })];
}

const body = [
  new Paragraph({ heading: HeadingLevel.TITLE, children: [new TextRun("E-Drum Kit Manager")] }),
  new Paragraph({ children: [new TextRun({ text: "The complete guide, with step-by-step lessons", size: 32 })], spacing: { after: 240 } }),
  p("For Windows and Mac, with REAPER. Version 1.5."),
  h2("Moving around this guide"),
  p("The guide uses headings at two levels:"),
  ...bullets([
    "**Heading level 1** starts each chapter, like \"Lesson 3: Choose a kit and play.\"",
    "**Heading level 2** starts each part inside a chapter, like \"Listen before you save.\"",
  ]),
  p("With VoiceOver in Word on a Mac, press **VO+Command+H** to move to the next heading, and **VO+Command+Shift+H** to go back. (VO means the VoiceOver keys: Control+Option, or Caps Lock if you've set it up that way.)"),
  p("Most of this guide works the same on Windows and on a Mac. Where something is different on a Mac, the guide says so, and Lesson 1 has a separate part for each. There's a list of Mac keys in the chapter \"Keyboard shortcuts.\""),
  p("Links to websites are marked as links. Press Enter on one to open it in your web browser."),
  new Paragraph({ children: [new PageBreak()] }),
  h1First("Contents"),
  new TableOfContents("Contents", { hyperlink: true, headingStyleRange: "1-2", pageNumbersEntryLevelsRange: "1-2" }),

  h1("1. Welcome"),
  p("E-Drum Kit Manager lets you choose how your electronic drum kit sounds. You pick a drum set from a simple list, press Enter, and your pads start playing that set. You can also build your own drum sets, change the sound of each drum, and switch sets right from your drum module, without going back to the computer."),
  p("It works together with these programs:"),
  ...bullets([
    "**EZdrummer 3** makes the drum sounds. It holds recordings of real drums, played in real studios. It's sold by Toontrack: [EZdrummer 3 on toontrack.com](https://www.toontrack.com/product/ezdrummer-3/).",
    "**REAPER** is the program EZdrummer runs inside. It takes the signals from your drum module and sends them to EZdrummer. You can try it free, and a personal license is inexpensive: [download REAPER](https://www.reaper.fm/download.php).",
    "**OSARA** is a free add-on that makes REAPER work with screen readers: [download OSARA](https://osara.reaperaccessibility.com/).",
    "**SWS Extension** is a free add-on for REAPER. E-Drum Kit Manager uses it to set the metronome's volume. It's optional: [download SWS](https://www.sws-extension.org/).",
  ]),
  p("E-Drum Kit Manager sits on top of all of them and does the fiddly parts for you. EZdrummer's own screens can't be used with a screen reader at all, so E-Drum Kit Manager gives you a way to do everything without them."),
  h2("What you can do with it"),
  ...bullets([
    "Choose a drum set from a list and play it right away. About 95 drum modules work straight away, because EZdrummer already knows them: most Alesis, Roland and Yamaha modules, and many from 2box, ddrum, EFNOTE, KAT, Pearl, Simmons and others. Any other module works after a five-minute set-up called Learn my kit (Lesson 2).",
    "Build your own drum set: pick each drum and cymbal, tune it higher or lower, and set how loud it is. A drum set is only about the sounds. Telling Kit Manager which pad sends what is a separate, one-time step for your module (Lesson 1 and Lesson 2), and every drum set then works with it.",
    "Make a quiet or worn pad play as loud as the others.",
    "Get a cross-stick sound from a soft hit on the snare rim, and a rimshot from a hard one.",
    "Change drum sets with the kit buttons on your drum module.",
    "Practice with a built-in metronome.",
    "Browse thousands of EZdrummer grooves by ear and play along with them.",
    "Record your playing in REAPER, then hear it back with any kit.",
    "Use it on Windows with JAWS or NVDA, or on a Mac with VoiceOver.",
    "Use almost any electronic drum module. It knows the same 95 or so modules that EZdrummer 3 does, and it can learn any other module.",
    "Save a backup copy of all the settings on a Roland TD-12, and put them back later.",
  ]),

  h1("2. Words you will see"),
  p("Here are the words this guide uses, explained in everyday language."),
  ...bullets([
    "**Drum module:** the box your pads plug into. Some people call it the brain.",
    "**Pad:** anything you hit: the snare, a tom, a cymbal, the kick pad.",
    "**Zone:** a part of a pad that makes its own sound. A snare pad has a head zone and a rim zone. A cymbal can have a bow (top) zone, an edge zone and sometimes a bell zone.",
    "**MIDI:** the language drum modules use to tell a computer what you played. MIDI isn't sound. It's a message, like \"snare, hit hard.\"",
    "**MIDI note:** a number your module sends for each pad or zone. For example, most modules send 38 for the snare head and 36 for the kick. E-Drum Kit Manager uses these numbers to know which drum you hit.",
    "**MIDI channel:** MIDI messages travel on 16 channels, a bit like TV channels. Almost every drum module sends its pads on channel 10.",
    "**USB cable or MIDI cables:** how the module talks to the computer. Some modules use a USB cable. Others use round 5-pin MIDI cables into an audio interface or a MIDI adapter.",
    "**Audio interface:** a box that gets sound out of the computer and into your headphones or speakers, usually with better sound and less delay than the computer's own headphone jack.",
    "**Velocity:** how hard you hit. The module sends a number from 1 (very soft) to 127 (as hard as possible).",
    "**DAW:** short for digital audio workstation, a program for recording and playing music. REAPER is a DAW.",
    "**Drum set or kit:** a full set of drums and cymbals. In this guide, a kit is one of the drum sets in E-Drum Kit Manager's list. Your module has its own kits too; the guide calls those module kits.",
    "**Room:** the place where the drums were recorded. EZdrummer 3 comes with three rooms: Main Room (a medium studio), Bright Room (big and open), and Tight Room (small and dry).",
    "**Room sound (room amount):** the echo of the room around the drums. More room sound makes the kit sound bigger and farther away. Less makes it close and dry.",
    "**Overheads:** microphones above the kit that pick up the whole set, mostly the cymbals. More overheads gives a brighter, more open sound.",
    "**Tuning:** how high or low a drum sounds. Tuning is set in semitones. One semitone is one step on a piano keyboard, and 12 semitones is a full octave.",
    "**Cross-stick:** the click you get by laying the stick across the snare and hitting the rim with it. Some people call it a rim click or side stick.",
    "**Rimshot:** hitting the head and the rim of the snare at the same time, for a loud crack.",
    "**One-shot:** a single percussion sound, like a cowbell, a clap or a wood block, that you can put on one of your extra pads.",
    "**E-drum map:** a list inside EZdrummer that says which drum each MIDI note plays. E-Drum Kit Manager sets this up for you.",
    "**Expansion (EZX):** an extra drum library you can buy for EZdrummer, with more drums and rooms.",
    "**Bulk dump:** a Roland feature that sends every setting in the module to the computer at once. E-Drum Kit Manager uses it to make backups.",
    "**Helper script:** a small add-on that E-Drum Kit Manager puts into REAPER, so the two programs can talk to each other.",
    "**Latency:** a small delay between hitting a pad and hearing the sound. Less is better.",
    "**OCR:** short for optical character recognition. Your screen reader takes a picture of the screen and reads out any words it finds, even in programs that don't work with screen readers.",
  ]),

  h1("3. Before you start"),
  h2("What you need"),
  ...bullets([
    "A Windows 10 or Windows 11 computer, or a Mac with Apple silicon (an M1 chip or newer) running macOS 14 or newer.",
    "REAPER, version 7 or newer: [download REAPER](https://www.reaper.fm/download.php).",
    "OSARA, so REAPER works with your screen reader: [download OSARA](https://osara.reaperaccessibility.com/).",
    "The SWS Extension for REAPER, if you want to set the metronome volume: [download SWS](https://www.sws-extension.org/).",
    "EZdrummer 3, with its core drum library installed: [EZdrummer 3 on toontrack.com](https://www.toontrack.com/product/ezdrummer-3/).",
    "An electronic drum kit, connected to the computer with a USB cable or MIDI cables.",
    "Headphones or speakers.",
  ]),
  h2("One important setting in REAPER"),
  p("EZdrummer comes in two versions on Windows, called VST2 and VST3. E-Drum Kit Manager only works with the **VST3** version. If you let E-Drum Kit Manager set up REAPER for you, it picks the right one. If you added EZdrummer to REAPER yourself, make sure you chose the one that says VST3."),
  Note("E-Drum Kit Manager isn't made by Toontrack (who make EZdrummer) or by any drum module maker. It works by reading and writing EZdrummer's own settings. A future EZdrummer update could change how that works, so keep backups of anything important."),

  h1("4. Lesson 1: Install everything and set up"),
  p("In this lesson you install the programs, connect them, and tell E-Drum Kit Manager about your drum module. You only do this once. Install the programs in the order shown. The first part is for Windows. If you have a Mac, skip ahead to the part called \"On a Mac.\""),
  h2("On Windows"),
  h3("Install REAPER"),
  ...steps([
    "Go to the [REAPER download page](https://www.reaper.fm/download.php) and download the Windows 64-bit installer.",
    "Open the file you downloaded and follow the installer. The normal choices are fine.",
    "Don't open REAPER yet.",
  ]),
  h3("Install OSARA"),
  ...steps([
    "Go to the [OSARA website](https://osara.reaperaccessibility.com/) and download the latest Windows installer.",
    "Make sure REAPER is closed, then open the OSARA installer and follow it.",
    "When it asks whether to replace REAPER's keyboard shortcuts with OSARA's, choosing Yes is usually best for screen reader users.",
  ]),
  h3("Install the SWS Extension (optional)"),
  ...steps([
    "Go to the [SWS website](https://www.sws-extension.org/) and download the Windows 64-bit installer.",
    "With REAPER closed, open it and follow the installer.",
  ]),
  h3("Install EZdrummer 3"),
  p("Toontrack installs EZdrummer and its sounds with a program called **Toontrack Product Manager**. Product Manager doesn't work with screen readers: your screen reader won't read its buttons. Here are three ways around that."),
  p("**Way 1: Use your screen reader's OCR**"),
  ...bullets([
    "**JAWS:** with Product Manager in front, press **Insert+Space**, then **O**, then **W** to read the whole window. Use the arrow keys to find a word, like Install, and press Enter to click it.",
    "**NVDA:** press **NVDA+R** to recognize the window. Use the arrow keys to find a word and press Enter to click it. Press Escape to close the results.",
    "After each click, the window changes, so recognize it again.",
    "Product Manager shows a list of your products with an Install or Update button next to each. Start with EZdrummer 3, then the EZdrummer 3 Core Library.",
  ]),
  p("**Way 2: Download the installers from Toontrack's website**"),
  ...steps([
    "In your web browser, go to [toontrack.com](https://www.toontrack.com/) and sign in.",
    "Open your account, then the list of your products.",
    "Download the Windows installer for EZdrummer 3, and the one for the EZdrummer 3 Core Library. These are big files, so they can take a while.",
    "Each download is a zip file. Open it, then run the installer inside. These are standard Windows installers, which read normally with a screen reader.",
  ]),
  p("**Way 3: Ask a sighted helper**"),
  p("If OCR is slow going, ask a sighted friend, or a visual interpreting service such as Aira or Be My Eyes, to help with the first install and sign-in. After that, you rarely need Product Manager again, except for updates and new expansions."),
  Note("The first time EZdrummer runs, it may ask you to sign in to your Toontrack account to authorize it. This screen doesn't work with screen readers either, so use OCR or a sighted helper for this step too."),
  h3("Install E-Drum Kit Manager"),
  ...steps([
    "Find the file called **EDrumKitManager-Setup** and open it.",
    "If Windows says **Windows protected your PC**, choose **More info**, then **Run anyway**. Windows shows this for programs from small publishers that haven't paid for a signing certificate.",
    "Follow the installer. It doesn't need an administrator password.",
    "If you want a desktop shortcut, leave that choice turned on.",
    "When the installer finishes, start E-Drum Kit Manager from the desktop or the Start menu.",
  ]),
  h3("Connect it to REAPER"),
  p("The first time it starts, E-Drum Kit Manager puts a small helper script into REAPER. REAPER only notices the helper when it starts, so:"),
  ...steps([
    "Close REAPER if it's open.",
    "Open REAPER again.",
    "From now on, the helper starts by itself whenever REAPER starts.",
  ]),
  h3("Tell it about your drum module"),
  p("On the first start, the **Hardware setup** window opens. Later, you can find it in the Drum module menu (Alt+M)."),
  ...steps([
    "In the **Drum module** list (Alt+M), choose yours. The list has every module EZdrummer 3 knows, by maker and model, for example Alesis Strike Pro, Roland TD-17 or Yamaha DTX6 Series. Type the first letter of the maker to jump through the list. If your module isn't there, see Lesson 2.",
    "Choose the **MIDI input** (Alt+I) your module is connected to. If you use a USB cable, look for your module's name. If you use MIDI cables, choose your audio interface or MIDI adapter.",
    "Choose the **MIDI output** (Alt+O) too. You only need it for backups.",
    "Press **Alt+S** to save.",
  ]),
  TipP("If the MIDI lists are empty, or your pads make no sound, REAPER isn't listening to your MIDI connection yet. In REAPER, open Options, then Preferences, then Audio, then MIDI Inputs. Select your module or interface, press the Applications key (or Shift+F10), and choose **Enable input for track record input**. Then open Hardware setup again."),
  h3("Check that everything is talking"),
  p("Press **Alt+C** (Check connection), or choose it in the EZdrummer menu. E-Drum Kit Manager should say that REAPER is connected. If it says REAPER didn't answer, make sure REAPER is open, and that you restarted it once after installing."),

  h2("On a Mac"),
  p("The Mac programs are installed a little differently. Many come as a disk image, a file ending in .dmg. When you open one, it appears like a drive in Finder, and you copy the program from it into your Applications folder."),
  h3("Install REAPER"),
  ...steps([
    "Go to the [REAPER download page](https://www.reaper.fm/download.php) and download the macOS version for Apple silicon (it may say ARM64 or universal).",
    "Open the .dmg file. Copy REAPER into your Applications folder: select it and press **Command+C**, open Applications with **Command+Shift+A**, and press **Command+V**. You can also drag it there.",
    "Open REAPER once from Applications, so it sets itself up, then quit it with **Command+Q**.",
  ]),
  h3("Install OSARA"),
  ...steps([
    "Go to the [OSARA website](https://osara.reaperaccessibility.com/) and download the latest Mac version.",
    "Open the file you downloaded. Inside is the OSARA installer. Make sure REAPER isn't running, then open the installer and follow it.",
    "When it asks whether to replace REAPER's keyboard shortcuts with OSARA's, choosing Yes is usually best for VoiceOver users.",
  ]),
  h3("Install the SWS Extension (optional)"),
  ...steps([
    "Go to the [SWS website](https://www.sws-extension.org/) and download the macOS version for Apple silicon (arm64).",
    "Open the .dmg file. Copy the file whose name starts with **reaper_sws** and ends in **.dylib**.",
    "In Finder, press **Command+Shift+G**, type **~/Library/Application Support/REAPER/UserPlugins**, and press Return.",
    "Press **Command+V** to paste the file there. Open REAPER, and SWS is loaded.",
  ]),
  h3("Install EZdrummer 3"),
  p("On a Mac, too, EZdrummer is installed with **Toontrack Product Manager**, and Product Manager doesn't work with VoiceOver. These are the ways around it:"),
  ...bullets([
    "**VoiceOver Screen Recognition:** VoiceOver can read the words in a program's window even when the program isn't made for it. Open VoiceOver Utility (VO+F8), choose Visuals, then VoiceOver Recognition, and turn on Screen Recognition. Then move through Product Manager with the VoiceOver keys and press VO+Space on Install.",
    "**Toontrack's website:** sign in at [toontrack.com](https://www.toontrack.com/), open your account, then your list of products, and download the Mac installers for EZdrummer 3 and the EZdrummer 3 Core Library. Open each one and follow the standard Mac installer, which works with VoiceOver.",
    "**A sighted helper:** ask a friend to do the first install and sign-in with you.",
  ]),
  Note("The first time EZdrummer runs, it may ask you to sign in to your Toontrack account to authorize it. Use Screen Recognition or a sighted helper for this step too."),
  h3("Install E-Drum Kit Manager"),
  ...steps([
    "Open the file **EDrumKitManager.dmg**.",
    "Copy **E-Drum Kit Manager** into your Applications folder, the same way as REAPER.",
    "Open it from Applications. The first time, your Mac may say it can't check the app for harmful software, because the app doesn't come from the App Store. If so, open System Settings, then Privacy & Security. Near the bottom, find the message about E-Drum Kit Manager and press **Open Anyway**. Your Mac asks once more; choose Open. After that, it opens normally.",
  ]),
  h3("Set up REAPER for your drums"),
  ...steps([
    "Open REAPER, then open its settings with **Command+Comma**.",
    "Go to **Audio**, then **Device**. Choose your audio interface for both input and output, instead of the default device. REAPER keeps asking about the audio device each time it starts until you do this.",
    "On the same page, set **Request block size** to 64 for the shortest delay between hitting a pad and hearing it. If you hear crackles, try 128. This setting can only be changed while REAPER is stopped.",
    "Go to **MIDI Inputs**. Select your module or interface, press **VO+Shift+M** to open its menu, and choose **Enable input for track record input**. Without this, REAPER hears nothing from your pads.",
    "Press **Return** to save the settings.",
  ]),
  h3("Connect it to REAPER"),
  p("The first time it starts, E-Drum Kit Manager puts a small helper script into REAPER. Quit REAPER with **Command+Q** and open it again. From then on, the helper starts by itself."),
  h3("Tell it about your drum module"),
  p("On the first start, **Hardware setup** opens. Later, you can find it in the Tools menu. Press **Tab** to move between its parts."),
  ...steps([
    "In the **Drum module** list, choose yours. Interact with the list with VO+Shift+Down Arrow, or press VO+Space to open it, then move with the arrow keys.",
    "Choose the **MIDI input** your module is connected to, and the **MIDI output** if you want backups.",
    "Press **Return** to save.",
  ]),
  TipP("On a Mac, Hardware setup also has an **Edit pad notes** button. It lists every part of your kit with the MIDI note it uses. For each one, you can press **Hit pad** and hit the pad to fill in its note, or **Play note** to hear what that note plays. It's a quick way to fix a single pad without running Learn my kit again."),
  h3("Check that everything is talking"),
  p("Move to the **Check REAPER connection** button and press VO+Space. E-Drum Kit Manager should say that REAPER is connected."),

  h1("5. Lesson 2: Set up a module that isn't in the list"),
  p("If your module isn't in the Drum module list, or the pads play the wrong drums, you can teach E-Drum Kit Manager your module by hitting each pad once. This is called **Learn my kit**. It takes about five minutes."),
  ...steps([
    "Make sure REAPER is open and your module is connected.",
    "Open the Drum module menu (Alt+M) and choose **Hardware setup**. On a Mac, it's in the Tools menu.",
    "Choose your module's MIDI input, then press **Alt+L** (Learn a new module).",
    "Type your module's name, and how many kits it has.",
    "Press **Alt+S** (Start learning).",
    "Your screen reader tells you what to hit, for example, \"Hit the middle of the snare head.\" Hit it firmly, once. It tells you the note it heard and moves on to the next one.",
    "If your kit doesn't have that part, like a fourth tom or a second crash, press **Alt+K** (Skip this one).",
    "When it's finished, your module is saved. Choose it in Hardware setup and press **Save**.",
  ]),
  h2("About the hi-hat"),
  p("You're asked to hit the hi-hat top and edge twice: once with the pedal pressed down (closed) and once with it up (open). You're also asked to step on the pedal. Many modules send the same note either way, which is fine. E-Drum Kit Manager handles it."),
  Note("On a Mac, Learn a new module is a button in Hardware setup. Use Tab to reach it and the other buttons, and press Return or VO+Space."),
  TipP("If one pad plays the wrong drum after learning, run Learn my kit again and hit that pad more firmly. Very soft hits can be missed."),

  h1("6. Lesson 3: Choose a kit and play"),
  p("This is what you'll do most of the time."),
  ...steps([
    "Open REAPER first, then E-Drum Kit Manager.",
    "You land in the **kit list**. You can always get back to it with **Alt+I**.",
    "Use the Up and Down arrow keys to hear the names of the kits.",
    "Press **Enter** on the kit you want.",
    "Wait a moment. Your screen reader says when the kit is loaded.",
    "Hit your pads. You're playing the new kit.",
  ]),
  p("To find out which drums are in a kit, press **Alt+D** to read the kit details, or press **Tab** once from the kit list. The details list every drum and cymbal and the room it came from. Press Shift+Tab to go back to the kit list."),
  TipP("**On a Mac:** press **Return** instead of Enter to load a kit. Use Tab to move between the kit list, the kit details and the buttons. The menu bar has every command: press VO+M to reach it."),
  p("Every command is also in the menu bar. Press Alt to reach it: **File**, **Kits**, **Practice** (grooves and metronome), **Drum module** (hardware setup, pad levels, MIDI and backups), **EZdrummer** (import, map and mixer, connection) and **Help**."),
  h2("The kits that come with it"),
  p("The list starts with the seven drum sets that come with EZdrummer 3:"),
  ...bullets([
    "**C&C Player Date (Main Room):** a warm, all-round kit. A good place to start.",
    "**Yamaha Absolute Hybrid Maple (Main Room):** modern and punchy, for pop and rock.",
    "**Gretsch USA Custom (Bright Room):** big and full, with lots of room sound.",
    "**Ludwig Classic 3-Ply Maple (Bright Room):** a classic rock sound in a large room.",
    "**Premier Resonator (Tight Room):** close and dry, with a vintage feel.",
    "**Ludwig Hollywood Maple (Tight Room):** small, dry and controlled.",
    "**Gretsch USA Custom Round Badge (Tight Room):** a small vintage kit, good for jazz and soft playing.",
  ]),
  h2("When your kit and the drum set don't match"),
  p("Real drum sets and electronic kits come in all sizes. A drum set in the list might have more or fewer toms and cymbals than you have pads. E-Drum Kit Manager handles both:"),
  ...bullets([
    "**You have more pads than the drum set has drums:** the extra pads borrow a drum of the same kind from another set recorded in the same room. Every pad still makes a sound.",
    "**You have fewer pads than the drum set has drums:** the extra drums are simply not played. If you'd rather hear a different tom or cymbal, edit the kit (Lesson 5) and choose which drums your pads play.",
  ]),
  h2("Your status messages"),
  p("Every message E-Drum Kit Manager gives you is spoken by your screen reader as it happens. If you miss one, press **F6** to go to the status line, which shows the last message. On a Mac, VoiceOver speaks each message, and the Status line is at the bottom of the window; use Tab to reach it."),

  h1("7. Lesson 4: Change kits from your drum module"),
  p("You don't have to go back to the computer to change kits. Your module's kit up and down buttons (on many modules, plus and minus) move through E-Drum Kit Manager's kit list, one kit at a time."),
  ...steps([
    "Sit at your kit. Press your module's kit up or kit down button.",
    "The next kit in the list loads. If E-Drum Kit Manager is open, it says the kit's name.",
    "Wait for the name before you play: loading a kit can take a second or two.",
  ]),
  TipP("Put your favorite kits at the top of the list so they're easy to reach. Select a kit and press **Alt+Up** or **Alt+Down** to move it."),
  TipP("If the buttons stop changing kits, your module is probably showing one of its settings screens, where plus and minus change a setting instead. Go back to its main kit screen. On a Roland TD-12, that's the large **KIT** button just left of the minus button."),
  Note("This is on unless you turn it off. To turn it off, for example while you use your module's own practice songs, press **Alt+W** in the main window or use the Drum module menu. Only kit changes on your module's drum channel count, usually channel 10, so most practice songs can't switch your kit by accident."),

  h1("8. Lesson 5: Build your own kit"),
  p("In this lesson you make a new kit and choose each drum yourself."),
  h2("Start a new kit"),
  p("There are two ways to start:"),
  ...bullets([
    "**From scratch:** press **Ctrl+N** (New kit).",
    "**From a kit you like:** select it and press **Ctrl+D** (Duplicate), then select the copy and press **F2** to edit it. This is usually easier.",
  ]),
  h2("The kit editor, from top to bottom"),
  ...bullets([
    "**Kit name (Alt+N):** the name you'll see in the list. Each kit needs its own name.",
    "**Switch all drums to room (Alt+R, then Alt+W):** choose a room, then press Switch drums. Every drum is swapped for one recorded in that room. It's a quick way to change the whole feel of a kit.",
    "**Room amount (Alt+A):** how much room echo. 100 is normal, 0 is completely dry, and 200 is double.",
    "**Overheads amount (Alt+O):** how much of the overhead microphones. Raise it for brighter cymbals, lower it for a tighter sound.",
    "**Percussion one-shot (Alt+H):** puts a cowbell, clap, wood block or another percussion sound on one of your extra pads instead of its drum.",
    "**One line per drum (Tab):** for the kick, snare, hi-hat, each tom, the ride and each crash, choose the sound, its tuning and its level.",
  ]),
  h2("Choose each drum"),
  ...steps([
    "Press **Tab** until you reach a drum, for example **Snare**.",
    "Use the Up and Down arrow keys to go through the snares you have. Each name says the size, the maker and the room, for example: 6.5x14\" Ludwig Supraphonic, Bright Room.",
    "Press **Tab** to move to **Tune**. Type a number from -12 to 12, or use the arrow keys. 0 is the drum as it was recorded. 2 is a little higher, and -3 is lower and fatter.",
    "Press **Tab** to move to **Level**. 100 is normal. 120 makes that drum louder in this kit, and 80 makes it softer.",
    "Do the same for each drum you want to change.",
  ]),
  TipP("Drum sizes are written as depth by width, in inches. A 14x22 kick is 14 inches deep and 22 inches across. Bigger drums sound lower and deeper."),
  h2("Listen before you save"),
  ...steps([
    "Press **Alt+P** (Preview in REAPER). Your kit loads in EZdrummer without being saved.",
    "Play your pads and listen.",
    "Change anything you like, then preview again.",
    "When you're happy, press **Alt+S** to save. Press Escape to leave without saving.",
  ]),
  h2("Add a cowbell or clap"),
  ...steps([
    "In the kit editor, press **Alt+H** to go to **Percussion one-shot**.",
    "Choose a sound, for example **Cowbell** or **Group claps**.",
    "Press **Tab** to go to **on pad**, and choose which extra pad plays it: Aux 1 or Aux 2.",
    "Press **Tab** again to set its level.",
    "Preview, then save.",
  ]),
  Note("On a Mac, the kit editor opens as a sheet over the main window. The Alt keys above aren't used; press Tab to move from one setting to the next. **Return** saves the kit, and **Escape** cancels. Press Command+N for a new kit and Command+D to duplicate one."),

  h1("9. Lesson 6: Cross-sticks and rimshots"),
  p("Many drum modules send the same signal for a rimshot and a cross-stick. If yours does, E-Drum Kit Manager tells them apart by how hard you hit the rim:"),
  ...bullets([
    "A **soft** hit on the snare rim plays a cross-stick.",
    "A **hard** hit plays a rimshot.",
  ]),
  p("Both are made a little louder automatically, so they cut through like they do on a real kit. If your module has its own cross-stick setting and sends a separate signal for it, E-Drum Kit Manager uses that instead."),
  h2("How to play a cross-stick"),
  ...steps([
    "Rest the butt end of the stick on the rim nearest you, with the stick lying across the snare pad.",
    "Keep the butt end where it is and tap the rim on the far side with the shoulder of the stick, gently, the way you would on an acoustic snare.",
    "You should hear a woody click.",
  ]),
  p("Some drummers do it the other way round, with the tip on the head and the back of the stick tapping the far rim. Both work: what matters to the module is a soft hit on the rim."),
  TipP("If you get rimshots when you want cross-sticks, play the cross-stick a little softer. If soft rimshots come out as cross-sticks, hit them a little harder."),
  Note("Some modules also send a quiet snare sound right after a rimshot. E-Drum Kit Manager removes it, so a rimshot doesn't sound doubled."),

  h1("10. Lesson 7: Make every pad sound even"),
  p("Older pads, or pads from different makers, often play quieter or louder than the rest. A cymbal pad that's a few years older than the others might need a harder hit to sound right. Pad levels fix this for every kit at once."),
  ...steps([
    "Make sure REAPER is open.",
    "Press **Alt+J** (Adjust pad levels). You can also find it in the Drum module menu.",
    "Each pad has a number. 100 means no change. 130 makes that pad play 30 percent harder than you hit it, and 80 makes it play softer. You can choose from 25 to 300.",
    "Leave the window open, and hit the pad while you change its number. The change works right away.",
    "Press **Alt+R** to put every pad back to 100 if you want to start over.",
    "Press **Close** when you're done. The levels are saved automatically.",
  ]),
  h2("Pad levels or drum level?"),
  ...bullets([
    "**Pad levels** fix your hardware. They apply to every kit.",
    "**Level** in the kit editor is a choice about the sound. It applies to one drum in one kit. For example, you might want a louder ride in your jazz kit only.",
  ]),
  p("The two work together. A pad at 120 with a drum level of 110 plays a little louder than either one alone."),

  h1("11. Lesson 8: Practice with the metronome"),
  p("The metronome plays a click in your headphones to help you keep time. It's in the main window, below the buttons, and in the Practice menu."),
  ...steps([
    "Press **Alt+O** to go to the tempo, and type how many beats per minute you want, for example 90.",
    "Press **Alt+G** to choose the time signature, for example 4/4 or 3/4.",
    "Press **Alt+V** to set how loud the click is.",
    "Press **F5** to start. Press F5 again to stop.",
  ]),
  h2("Tap the tempo"),
  p("If you know how a song feels but not its tempo, tap it. Press **Alt+A** (Tap tempo) again and again, in time with the beat. After a few taps the tempo is set. If you stop for two seconds, the next tap starts a new count."),
  TipP("You can change the tempo while the metronome is running. Slow songs down to learn them, then speed them up a little at a time."),
  Note("The volume setting needs the free SWS Extension (see Lesson 1). Without it, the click plays at REAPER's normal volume."),

  h1("12. Lesson 9: Play along with grooves"),
  p("EZdrummer comes with thousands of grooves: short drum parts played by real drummers, in every style from ballads to metal. Kit Manager lets you browse them by ear, play them through your own kit in REAPER, and practise along."),
  h2("Find a groove"),
  ...steps([
    "Make sure REAPER is open.",
    "Press **Ctrl+G**, or open the Practice menu and choose **Grooves**. The Grooves window opens beside the main window.",
    "Press **Alt+G** to go to the list. It works like folders: Up and Down move, **Right** opens an item and **Left** closes it.",
    "The levels are: library (EZdrummer 3, or an expansion), style (Funk, Blues, Metal...), feel (straight 4/4, swing 4/4, 6/8...), song with its tempo (for example Diva Ballad, 57 BPM), part (Intro, Verse, Chorus, Fills, Endings...), and variation.",
  ]),
  TipP("To find something quickly, press **Alt+F** and type words, for example **funk**, **shuffle** or **jazz 120**. A number finds songs within 5 BPM of it. Press Enter to jump to the list."),
  h2("Play it"),
  ...steps([
    "Move to a variation, or to a part or song (Kit Manager then plays its first variation).",
    "Press **Enter**. The groove plays in REAPER on a loop, with the kit you've loaded. Kit Manager says its name, time signature, number of bars and tempo.",
    "Press **Space** to stop, and Space again to start. The **Play** button (Alt+P) does the same: it says Stop while a groove plays. **F5** also works.",
  ]),
  h2("Slow it down or speed it up"),
  p("Press **Alt+T** for the tempo and type a number, or press Up and Down. Each new song starts at its own tempo. While a groove plays, the change happens straight away, so you can learn a groove slowly and then bring it up to speed."),
  p("Uncheck **Loop** (Alt+L) to hear a groove once instead of over and over."),
  h2("Find a groove by playing it"),
  p("Instead of browsing, you can play a beat on your pads and let Kit Manager find grooves that sound like it."),
  ...steps([
    "Press **Alt+B** (Find by playing).",
    "When Kit Manager says so, play a groove on your pads for two bars or more, at the speed you want, then stop. Start on beat one, usually with the kick.",
    "Two seconds after your last hit, Kit Manager searches and lists the closest grooves, best first, each with how closely it matches in percent.",
    "Press **Enter** on one to play it. Clear the Find box (Alt+F, then delete the text) to see all grooves again.",
  ]),
  TipP("Kick, snare and hi-hat or ride matter most. A steady pattern you repeat a few times finds better matches than a single bar."),
  h2("Play a whole song"),
  p("Each song in the list has parts: intro, verses, choruses, bridge, fills and endings. **Play whole song** (Alt+W) strings them together like a real arrangement: intro, verse, chorus, second verse, chorus, bridge, last chorus and ending, with a fill leading into each chorus. Kit Manager says each section's name as it starts, so you always know where you are. It plays once, about two to five minutes depending on the tempo, and says when the song has ended."),
  p("Select the song, or any part or variation inside it, then press Alt+W."),
  h2("Change how a groove is played"),
  ...bullets([
    "**Style** (Alt+Y): Normal, Hi-hat moved to ride, Ride moved to hi-hat, Half time (twice as long and laid back) or Double time (twice as busy).",
    "**Dynamics** (Alt+D): Soft, Normal or Hard.",
  ]),
  p("The choices apply to the next groove or song you play, and work with Play whole song too."),
  h2("How to practise with grooves"),
  ...bullets([
    "**Listen, then copy:** play a groove a few times, stop it, then play it yourself.",
    "**Play along:** let it loop and play the same groove with it. When you're together, the two sound like one drummer.",
    "**Learn a song's parts:** go through a song's Intro, Verse, Chorus and Fills in order.",
  ]),
  Note("The Grooves window is on Windows for now; it is coming to the Mac version."),
  Note("The groove plays on your EZdrummer track and is removed when you stop it, so it doesn't stay in your REAPER project. If you record while a groove plays, only your pads are recorded."),

  h1("13. Lesson 10: Record yourself playing"),
  p("Recording lets you listen back to your playing, check your timing, or save a song idea. You record in REAPER, with the kit you've chosen in E-Drum Kit Manager."),
  p("REAPER records what you play as MIDI: which pad you hit, when, and how hard. It doesn't record the sound itself. That has a big advantage: after you record, you can load a different kit and your recording plays back with the new drums."),
  h2("Get ready"),
  ...steps([
    "Load a kit in E-Drum Kit Manager, as usual.",
    "Switch to REAPER with **Alt+Tab** (on a Mac, **Command+Tab**).",
    "Move to the drum track with the Up and Down arrow keys. E-Drum Kit Manager made this track for you; it has EZdrummer on it.",
    "Check that the track is armed for recording. OSARA says \"armed\" when you move to it. E-Drum Kit Manager arms it for you, because REAPER only lets you hear your pads through an armed track. Don't turn arming off, or your pads go silent.",
    "If you want a click while you record, start the metronome in E-Drum Kit Manager first (F5). The click isn't recorded.",
  ]),
  h2("Record"),
  ...steps([
    "Press **Home** to go to the start of the project. On a Mac laptop keyboard, Home is **Fn+Left Arrow**.",
    "Press **Ctrl+R** to start recording. On a Mac, press **Command+R**.",
    "Play.",
    "Press **Space** to stop. Your take is saved on the track.",
  ]),
  h2("Listen back"),
  ...steps([
    "Press **Home**, then **Space** to play your recording. Press Space again to stop.",
    "Not happy with it? Press **Ctrl+Z** (on a Mac, **Command+Z**) to undo the take, then record again.",
    "Want to hear it with a different kit? Switch to E-Drum Kit Manager, load another kit, and play the recording again.",
  ]),
  h2("Keep it"),
  ...bullets([
    "**Save the project** with **Ctrl+S** (on a Mac, **Command+S**). REAPER asks for a name the first time. This keeps your recording so you can change it later.",
    "**Make a sound file** to share or play on your phone: press **Ctrl+Alt+R** (on a Mac, **Command+Option+R**) to open REAPER's Render window. Choose WAV or MP3 as the format, choose where to save it, and press the Render button. Rendering turns your drums into a normal sound file, using the kit that's loaded at the time.",
  ]),
  Note("To record along with a song, play the song on the computer while you record (see \"Playing along with music\"). Only your drums are recorded, not the song."),

  h1("14. Lesson 11: Back up and restore your module"),
  p("Your module holds your module kits, pad settings and setup. A backup saves all of it on your computer, so you can put it back if something goes wrong or you reset the module."),
  Note("Backups work with the Roland TD-12 for now. You need MIDI cables going both ways: from the module's MIDI Out to the computer, and from the computer to the module's MIDI In."),
  h2("Make a backup"),
  ...steps([
    "Make sure REAPER is open.",
    "In E-Drum Kit Manager, press **Alt+B** (Back up module settings). It's also in the Drum module menu (on a Mac, the File menu).",
    "A message explains the steps for your module. Press **OK**.",
    "Start the bulk dump on the module. On the TD-12, press **SETUP**, then **F1** (MIDI), then **F5** (BULK), then **F5** again (EXECUTE).",
    "Wait. A full backup can take a few minutes. E-Drum Kit Manager tells you when it's saved and whether every part arrived safely.",
  ]),
  p("Backups are saved in your Documents folder, in a folder called **TD-12 Backups**. Each file has the date and time in its name."),
  h2("Restore a backup"),
  ...steps([
    "Press **Alt+R** (Restore module backup). It's also in the Drum module menu (on a Mac, the File menu).",
    "Choose the backup file you want.",
    "You're warned that this replaces the settings on the module. Choose **Yes** to go ahead.",
    "Don't touch the module while the backup is being sent. E-Drum Kit Manager tells you how long it will take, and tells you again when it's finished.",
  ]),

  h1("15. Lesson 12: Work with EZdrummer directly"),
  p("You never have to open EZdrummer's screens. But if a sighted friend changes something in EZdrummer for you, you can bring it into E-Drum Kit Manager."),
  h2("Bring in a kit made in EZdrummer"),
  ...steps([
    "Set up the kit in EZdrummer in REAPER.",
    "In E-Drum Kit Manager, open the EZdrummer menu (Alt+Z) and choose **Import current kit from EZdrummer**.",
    "The kit editor opens with that kit. Give it a name and press **Alt+S** to save.",
  ]),
  h2("Update the map and mixer"),
  p("The e-drum map (which pad plays which drum) and EZdrummer's mixer are shared by all your kits. If you change either one in EZdrummer, press **Alt+U** (Update map and mixer from EZdrummer). Every kit then uses the new settings."),

  h1("16. More drum sounds"),
  p("Toontrack sells extra drum libraries for EZdrummer, called expansions or EZX. Each one adds new drums, cymbals and often a new room. Browse them on [toontrack.com](https://www.toontrack.com/)."),
  ...steps([
    "Buy the expansion, then install it with Toontrack Product Manager or from the download on Toontrack's website. Product Manager doesn't work with screen readers; see \"Install EZdrummer 3\" in Lesson 1 for ways around that.",
    "Close and reopen E-Drum Kit Manager.",
    "E-Drum Kit Manager makes a ready-to-play kit for each drum set in the expansion and adds them to the end of your kit list. It tells you how many it added.",
    "The new drums also appear in the kit editor's drum lists, and the new room appears under Switch all drums to room.",
  ]),
  p("There's nothing else to set up. Your pads keep working the same way."),

  h1("17. Playing along with music"),
  p("You can play along with songs from Apple Music, Spotify, YouTube or any other player. Just play the music on the same computer. As long as your computer sends its sound to the same headphones or speakers as REAPER, you hear the music and your drums together."),
  Note("On a Mac, if REAPER uses your audio interface, set the Mac's sound output to the same interface: open System Settings, then Sound, then Output."),
  TipP("If the music is too loud compared to the drums, turn down the music player's own volume instead of your headphones."),

  h1("18. Keeping it up to date"),
  p("New versions fix problems and add features. On Windows, E-Drum Kit Manager checks for a new version each time it starts, a few seconds after it opens. It only speaks up when there is one."),
  ...steps([
    "When a new version is found, a message tells you which version is available and which one you have.",
    "Choose **Yes** to download and install it. E-Drum Kit Manager closes, updates itself, and opens again. Your kits and settings are kept.",
    "Choose **No** to skip that version. You won't be asked about it again when the program starts.",
    "Choose **Cancel** to decide later. You'll be asked again next time.",
  ]),
  p("To check yourself at any time, open the **Help** menu and choose **Check for updates**. To stop the automatic check, open the Help menu and uncheck **Check for updates at startup**."),
  Note("On a Mac, download new versions from the [E-Drum Kit Manager download page](https://github.com/samarositz-rgb/e-drum-kit-manager/releases/latest)."),

  h1("19. When something goes wrong"),
  ...table(["What happens", "What to try"], [
    ["\"REAPER did not answer\"", "Open REAPER. If you just installed or updated E-Drum Kit Manager, close REAPER and open it again, once."],
    ["Pads make no sound", "Press **Alt+X** (Reconnect drum module MIDI), then hit a pad. Check that the module is on and its cable is plugged in. This often happens after another program used your MIDI connection."],
    ["The wrong drum plays", "Make sure the right module is chosen in Hardware setup. If it still plays the wrong drums, use Learn my kit (Lesson 2). Also check that the module isn't playing one of its practice songs."],
    ["The kit changes by itself", "Your module may be sending kit changes, for example from a practice song. Turn off Step through kits (Alt+W)."],
    ["Crackles or pops in the sound", "Your computer is struggling to keep up. In REAPER, open Options, Preferences, Audio, Device (on a Mac, REAPER's settings with Command+Comma, then Audio, Device), and choose a larger buffer size. Close other programs, and use 48,000 Hz instead of 96,000 Hz."],
    ["A delay between hitting and hearing", "This is latency. In REAPER, choose a smaller buffer size, as long as it doesn't cause crackles. If you have an audio interface, use its own driver (called ASIO)."],
    ["A pad is too quiet", "Use Pad levels (Alt+J) to raise it."],
    ["Cross-sticks come out as rimshots", "Play the cross-stick more softly. See Lesson 6."],
    ["A new expansion doesn't show up", "Close E-Drum Kit Manager and open it again."],
    ["On a Mac: pads make no sound at all", "In REAPER's settings (Command+Comma), open MIDI Inputs and make sure your module or interface has **Enable input for track record input** turned on. Then choose Reconnect drum module MIDI from the Tools menu."],
    ["On a Mac: REAPER asks about the audio device every time", "Choose your audio interface in REAPER's settings, under Audio, then Device."],
    ["On a Mac: \"E-Drum Kit Manager can't be opened\"", "Open System Settings, then Privacy & Security, and press Open Anyway next to the message about E-Drum Kit Manager."],
  ], [3000, 6360]),

  h1("20. Keyboard shortcuts"),
  p("On Windows, **Alt+S** always means Save, in every window that has a Save button. The Mac keys are at the end of this chapter."),
  h2("Windows: main window"),
  ...table(["Key", "What it does"], [
    ["Alt", "Opens the menu bar: File, Kits, Practice, Drum module, EZdrummer and Help."],
    ["Alt+I", "Go to the kit list."],
    ["Enter (in the kit list)", "Load the selected kit in REAPER."],
    ["Tab (from the kit list), or Alt+D", "Read the kit details."],
    ["F2", "Edit the selected kit."],
    ["Ctrl+N", "New kit."],
    ["Ctrl+D", "Duplicate the selected kit."],
    ["Delete", "Delete the selected kit."],
    ["Alt+Up, Alt+Down", "Move the selected kit up or down the list."],
    ["F6", "Go to the status line, which shows the last message."],
    ["Alt+J", "Pad levels."],
    ["Alt+W", "Turn stepping with the module's kit buttons on or off."],
    ["Alt+X", "Reconnect drum module MIDI."],
    ["Alt+U", "Update the map and mixer from EZdrummer."],
    ["Alt+C", "Check the connection."],
    ["Alt+B, Alt+R", "Back up or restore your module."],
    ["F5", "Start or stop the metronome."],
    ["Alt+O, Alt+G, Alt+V", "Metronome tempo, time signature and volume."],
    ["Alt+A", "Tap tempo."],
    ["Ctrl+G", "Open the Grooves window."],
    ["F1", "Open the short user guide."],
  ], [3000, 6360]),
  h2("Windows: kit editor"),
  ...table(["Key", "What it does"], [
    ["Alt+N", "Kit name."],
    ["Alt+R, then Alt+W", "Choose a room, then switch all drums to it."],
    ["Alt+A", "Room amount."],
    ["Alt+O", "Overheads amount."],
    ["Alt+H", "Percussion one-shot."],
    ["Tab", "Move to the next drum, tuning or level."],
    ["Alt+P", "Preview in REAPER."],
    ["Alt+S", "Save."],
    ["Escape", "Cancel without saving."],
  ], [3000, 6360]),
  h2("Windows: Hardware setup"),
  ...table(["Key", "What it does"], [
    ["Alt+M", "Drum module list."],
    ["Alt+I", "MIDI input."],
    ["Alt+O", "MIDI output."],
    ["Alt+L", "Learn a new module."],
    ["Alt+S", "Save."],
  ], [3000, 6360]),

  h2("On a Mac"),
  p("The Mac version has no Alt-letter shortcuts. Use Tab to move between parts of a window, the VoiceOver keys to read, and the menu bar (VO+M) for every command."),
  ...table(["Key", "What it does"], [
    ["VO+M", "Go to the menu bar: File, Kit, Tools, Metronome and Help."],
    ["Return (in the kit list)", "Load the selected kit in REAPER."],
    ["F2", "Edit the selected kit. On a laptop keyboard you may need to hold Fn."],
    ["Command+N", "New kit."],
    ["Command+D", "Duplicate the selected kit."],
    ["Delete", "Delete the selected kit."],
    ["Option+Up, Option+Down", "Move the selected kit up or down the list."],
    ["F5", "Start or stop the metronome. You may need to hold Fn."],
    ["Up and Down arrows (in a number field)", "Change the number, for example the tempo."],
    ["Return (in a sheet)", "Save, in the kit editor, Hardware setup and Pad notes."],
    ["Escape (in a sheet)", "Cancel without saving."],
    ["Command+?", "Open the short user guide."],
    ["Command+Q", "Quit E-Drum Kit Manager."],
  ], [3000, 6360]),

  h1("21. Where your things are saved"),
  ...bullets([
    "**Your kits and settings on Windows:** in your user folder, under AppData, Roaming, EDrumKitManager. Uninstalling the program keeps them.",
    "**Your kits and settings on a Mac:** in your home folder, under Library, Application Support, EDrumKitManager. Deleting the app keeps them.",
    "**Module backups:** in Documents, TD-12 Backups, on both Windows and Mac.",
  ]),
  p("To move to a new computer, copy the EDrumKitManager folder to the same place on the new computer before you start E-Drum Kit Manager there for the first time. This works from Windows to Mac and back too, as long as both have the same EZdrummer sounds installed."),
];

const doc = new Document({
  creator: "E-Drum Kit Manager",
  title: "E-Drum Kit Manager: The Complete Guide",
  description: "Guide and tutorials for E-Drum Kit Manager on Windows with REAPER",
  styles: {
    default: { document: { run: { font: FONT, size: 24 } } },
    paragraphStyles: [
      { id: "Title", name: "Title", basedOn: "Normal", run: { font: FONT, size: 56, bold: true, color: "1F3864" },
        paragraph: { spacing: { before: 2400, after: 240 }, keepNext: true } },
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { font: FONT, size: 36, bold: true, color: "1F3864" }, paragraph: { spacing: { before: 240, after: 200 }, keepNext: true, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { font: FONT, size: 28, bold: true, color: "2E75B6" }, paragraph: { spacing: { before: 280, after: 120 }, keepNext: true, outlineLevel: 1 } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { font: FONT, size: 24, bold: true }, paragraph: { spacing: { before: 200, after: 80 }, keepNext: true, outlineLevel: 2 } },
    ],
  },
  numbering: {
    config: [
      { reference: "steps", levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
      { reference: "bullets", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
    ],
  },
  sections: [{
    properties: { page: { size: { width: 12240, height: 15840 }, margin: { top: 1440, bottom: 1440, left: 1440, right: 1440 } } },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun("Page "), new TextRun({ children: [PageNumber.CURRENT] })] })] }) },
    children: body,
  }],
});

const out = path.join(__dirname, "..", "docs", "E-Drum Kit Manager Guide.docx");
fs.mkdirSync(path.dirname(out), { recursive: true });
Packer.toBuffer(doc).then(b => { fs.writeFileSync(out, b); console.log(out); });
