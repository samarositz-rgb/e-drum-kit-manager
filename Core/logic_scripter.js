// E-Drum Kit Manager for Logic Pro: paste into a Scripter MIDI FX on the EZdrummer track.
// Does what the REAPER effects do: soft snare rim hits become cross-stick, rimshots and cross-sticks
// get a boost, the quiet head hit the module sends after a rimshot is dropped, and each pad's
// hit strength can be scaled. (GarageBand has no Scripter, so these extras are Logic only.)

var RIM = 40, HEAD = 38, CROSS = 37; // filled in by Kit Manager from the module profile
var PAD_LEVELS = {};                  // note -> percent, filled in by Kit Manager

var PluginParameters = [
  { name: "Rim split velocity (softer = cross-stick)", type: "lin", minValue: 1, maxValue: 127, numberOfSteps: 126, defaultValue: 52 },
  { name: "Ignore quiet head hit after rimshot (ms)", type: "lin", minValue: 0, maxValue: 200, numberOfSteps: 200, defaultValue: 60 },
  { name: "Quiet head hit max velocity", type: "lin", minValue: 1, maxValue: 127, numberOfSteps: 126, defaultValue: 50 },
  { name: "Cross-stick boost", type: "lin", minValue: 0, maxValue: 60, numberOfSteps: 60, defaultValue: 25 },
  { name: "Rimshot boost", type: "lin", minValue: 0, maxValue: 60, numberOfSteps: 60, defaultValue: 20 },
];

var lastRim = -1e9, rimToCross = false;

function scaled(pitch, velocity) {
  var pct = PAD_LEVELS[pitch];
  return pct === undefined ? velocity : Math.max(1, Math.min(127, Math.round(velocity * pct / 100)));
}

function HandleMIDI(event) {
  if (event instanceof NoteOn && event.velocity > 0) {
    var v = scaled(event.pitch, event.velocity);
    if (RIM >= 0 && event.pitch == RIM) {
      if (event.velocity < GetParameter(0)) {
        event.pitch = CROSS;
        v += GetParameter(3);
        rimToCross = true;
      } else {
        v += GetParameter(4);
        rimToCross = false;
        lastRim = Date.now();
      }
    } else if (event.pitch == HEAD && GetParameter(1) > 0 &&
               Date.now() - lastRim < GetParameter(1) && event.velocity <= GetParameter(2)) {
      return; // the module's echo of the rimshot
    }
    event.velocity = Math.min(127, v);
  } else if (event instanceof NoteOff && RIM >= 0 && event.pitch == RIM && rimToCross) {
    event.pitch = CROSS;
  }
  event.send();
}
