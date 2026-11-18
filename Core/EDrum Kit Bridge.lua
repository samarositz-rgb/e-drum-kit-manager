-- E-Drum Kit Bridge for REAPER
-- Installed and kept up to date by E-Drum Kit Manager. Started from __startup.lua.
-- Loads drum kits into EZdrummer 3 when the app asks, or when the TD-12 sends a program change.

-- Kit Manager's bridge folder: %APPDATA% on Windows, Application Support on a Mac.
local dir
if reaper.GetOS():find("Win") then
  dir = os.getenv("APPDATA") .. "\\EDrumKitManager\\bridge\\"
else
  dir = os.getenv("HOME") .. "/Library/Application Support/EDrumKitManager/bridge/"
end
local REQUEST = dir .. "request.txt"
local STATUS = dir .. "status.txt"
local PROGRAMS = dir .. "programs.txt"
local EZD = "EZdrummer 3"

local function readFile(path)
  local f = io.open(path, "rb")
  if not f then return nil end
  local s = f:read("a")
  f:close()
  return s
end

local function writeFile(path, s)
  local tmp = path .. ".tmp"
  local f = io.open(tmp, "wb")
  if not f then return end
  f:write(s)
  f:close()
  os.remove(path)
  os.rename(tmp, path)
end

local function lines(s)
  local t = {}
  for l in (s .. "\n"):gmatch("(.-)\r?\n") do t[#t + 1] = l end
  return t
end

-- Drum module settings written by Kit Manager (module.txt, key=value lines).
local function module(key, default)
  local f = io.open(dir .. "module.txt", "rb")
  if not f then return default end
  local s = f:read("a")
  f:close()
  local v = s:match("\n" .. key .. "=([^\r\n]*)") or s:match("^" .. key .. "=([^\r\n]*)")
  if v == nil or v == "" then return default end
  return tonumber(v) or v
end

local function status(id, ok, msg)
  writeFile(STATUS, id .. "\t" .. (ok and "OK" or "ERR") .. "\t" .. msg)
end

local function hasEzd(tr)
  for i = 0, reaper.TrackFX_GetCount(tr) - 1 do
    local _, name = reaper.TrackFX_GetFXName(tr, i, "")
    if name:find(EZD, 1, true) then return true end
  end
  return false
end

local function trackName(tr)
  local _, name = reaper.GetSetMediaTrackInfo_String(tr, "P_NAME", "", false)
  local num = math.floor(reaper.GetMediaTrackInfo_Value(tr, "IP_TRACKNUMBER"))
  if name == "" then return "track " .. num end
  return "track " .. num .. ", " .. name
end

local function findTarget()
  local sel = reaper.GetSelectedTrack(0, 0)
  if sel and hasEzd(sel) then return sel end
  for i = 0, reaper.CountTracks(0) - 1 do
    local tr = reaper.GetTrack(0, i)
    if hasEzd(tr) then return tr end
  end
end

local ALL_MIDI_INPUTS = 4096 + (63 << 5)

-- Track input for the drum module: its MIDI input and drum channel from Kit Manager's settings,
-- or every MIDI input on that channel if the input isn't found.
local function moduleInput()
  local channel = module("drumChannel", 10)
  local wanted = module("midiIn", "")
  if wanted ~= "" then
    for i = 0, 61 do
      local present, name = reaper.GetMIDIInputName(i, "")
      if present and name:find(wanted, 1, true) then return 4096 + (i << 5) + channel end
    end
  end
  return ALL_MIDI_INPUTS + channel
end

local function createTrack()
  local idx = reaper.CountTracks(0)
  reaper.InsertTrackAtIndex(idx, true)
  local tr = reaper.GetTrack(0, idx)
  reaper.GetSetMediaTrackInfo_String(tr, "P_NAME", "EZdrummer 3 (" .. module("name", "drums") .. ")", true)
  if reaper.TrackFX_AddByName(tr, "VST3i:EZdrummer 3 (Toontrack) (32 out)", false, -1) < 0
     and reaper.TrackFX_AddByName(tr, EZD, false, -1) < 0 then
    reaper.DeleteTrack(tr)
    return nil
  end
  reaper.SetMediaTrackInfo_Value(tr, "I_RECINPUT", moduleInput())
  reaper.SetMediaTrackInfo_Value(tr, "I_RECMON", 1)
  reaper.SetMediaTrackInfo_Value(tr, "I_RECARM", 1)
  return tr
end

-- Sends three snare hits (channel 10, note 38) to the MIDI output whose name contains `match`,
-- to check the cable from that output to the TD-12's MIDI IN.
local function midiOutTest(id, match)
  for i = 0, reaper.GetMaxMidiOutputs() - 1 do
    local ok, name = reaper.GetMIDIOutputName(i, "")
    if ok and name:find(match, 1, true) then
      for n = 0, 2 do
        reaper.StuffMIDIMessage(16 + i, 0x99, 38, 100)
        reaper.StuffMIDIMessage(16 + i, 0x89, 38, 0)
        if n < 2 then local t = reaper.time_precise() + 0.5 while reaper.time_precise() < t do end end
      end
      return status(id, true, "Sent 3 snare hits to " .. name)
    end
  end
  status(id, false, "No enabled MIDI output named " .. match .. ". Enable it in Preferences, MIDI Devices.")
end

-- Puts the snare rim split (soft rim = cross-stick) in front of EZdrummer, once.
local RIM_SPLIT = "JS:EDrumKitManager/snare_rim_split.jsfx"
local function ensureRimSplit(tr)
  local fx = -1
  for i = 0, reaper.TrackFX_GetCount(tr) - 1 do
    local _, name = reaper.TrackFX_GetFXName(tr, i, "")
    if name:find("Snare Rim Split", 1, true) then fx = i end
  end
  if fx < 0 then fx = reaper.TrackFX_AddByName(tr, RIM_SPLIT, false, -1000) end
  if fx < 0 then return false end
  -- Notes from the module profile; rim note -1 turns the split off (module has its own cross-stick note).
  local rim = module("rimSplit", 1) == 1 and module("rimNote", 40) or -1
  reaper.TrackFX_SetParam(tr, fx, 5, rim)
  reaper.TrackFX_SetParam(tr, fx, 6, module("headNote", 38))
  reaper.TrackFX_SetParam(tr, fx, 7, module("crossNote", 37))
  return true
end

-- Pad levels sit after the rim split, before EZdrummer: one percentage per MIDI note (0-127),
-- from the loaded kit's .levels file, which Kit Manager computes from the pad levels and the kit.
local PAD_LEVELS = "JS:EDrumKitManager/pad_levels.jsfx"
local currentKitLevels -- .levels file of the kit most recently loaded

local function percentages(path)
  local t = {}
  for _, l in ipairs(lines(readFile(path) or "")) do
    local v = tonumber(l)
    if v then t[#t + 1] = v end
  end
  return t
end

local function ensurePadLevels(tr)
  local fx, after = -1, 0
  for i = 0, reaper.TrackFX_GetCount(tr) - 1 do
    local _, name = reaper.TrackFX_GetFXName(tr, i, "")
    if name:find("Pad Levels", 1, true) then fx = i end
    if name:find("Snare Rim Split", 1, true) then after = i + 1 end
  end
  if fx < 0 then fx = reaper.TrackFX_AddByName(tr, PAD_LEVELS, false, -1000 - after) end
  if fx < 0 then return false end
  local levels = currentKitLevels and percentages(currentKitLevels) or {}
  if #levels < 128 then levels = {} end -- older 12-pad files: leave everything at 100
  for n = 1, 128 do
    reaper.TrackFX_SetParam(tr, fx, n - 1, math.max(25, math.min(300, levels[n] or 100)))
  end
  return true
end

-- Returns chunk lines and the index range of the EZdrummer state lines (exclusive of <VST and >).
local function ezdBlock(tr)
  local _, chunk = reaper.GetTrackStateChunk(tr, "", false)
  local t = lines(chunk)
  for i, l in ipairs(t) do
    if l:match("^%s*<VST ") and l:find(EZD, 1, true) then
      for j = i + 1, #t do
        if t[j]:match("^%s*>%s*$") then return t, i, j end
      end
    end
  end
  return t
end

local currentKitPath -- kit file most recently loaded, for stepping with the TD-12's kit buttons

local function applyKit(id, name, statePath)
  local data = readFile(statePath)
  if not data then return status(id, false, "Kit file not found: " .. statePath) end
  local tr = findTarget()
  local created = false
  if not tr then
    tr = createTrack()
    created = true
    if not tr then return status(id, false, "EZdrummer 3 could not be added to a track.") end
  end
  local t, s, e = ezdBlock(tr)
  if not s then return status(id, false, "EZdrummer 3 not found in the track chunk.") end
  local out = {}
  for i = 1, s do out[#out + 1] = t[i] end
  for _, l in ipairs(lines(data)) do
    if l ~= "" then out[#out + 1] = l end
  end
  out[#out + 1] = t[e]
  out[#out + 1] = 'PRESETNAME "' .. (name:gsub('"', "'")) .. '"'
  local rest = e + 1
  if t[rest] and t[rest]:match("^%s*PRESETNAME ") then rest = rest + 1 end
  for i = rest, #t do out[#out + 1] = t[i] end

  reaper.Undo_BeginBlock()
  reaper.PreventUIRefresh(1)
  local ok = reaper.SetTrackStateChunk(tr, table.concat(out, "\n"), false)
  reaper.PreventUIRefresh(-1)
  reaper.Undo_EndBlock("Load drum kit: " .. name, -1)
  if not ok then return status(id, false, "REAPER rejected the kit.") end
  ensureRimSplit(tr)
  currentKitLevels = statePath:gsub("%.txt$", ".levels")
  ensurePadLevels(tr)
  -- If the track listens to a MIDI device that is no longer connected (like an unplugged adapter),
  -- switch it to the module's MIDI input from Kit Manager's settings, or to all inputs.
  local input = math.floor(reaper.GetMediaTrackInfo_Value(tr, "I_RECINPUT"))
  local device = (input >> 5) & 63
  if input >= 4096 and device < 62 and not reaper.GetMIDIInputName(device, "") then
    reaper.SetMediaTrackInfo_Value(tr, "I_RECINPUT", moduleInput())
  end
  currentKitPath = statePath
  status(id, true, "Loaded " .. name .. " on " .. trackName(tr) .. (created and " (new track, armed for your drums)" or ""))
end

local function captureKit(id, outPath)
  local tr = findTarget()
  if not tr then return status(id, false, "No track with EZdrummer 3 found.") end
  local t, s, e = ezdBlock(tr)
  if not s then return status(id, false, "EZdrummer 3 not found in the track chunk.") end
  local body = {}
  for i = s + 1, e - 1 do body[#body + 1] = t[i]:match("^%s*(.-)%s*$") end
  writeFile(outPath, table.concat(body, "\n"))
  status(id, true, "Captured EZdrummer from " .. trackName(tr))
end

-- Metronome: sets the project tempo and time signature, turns REAPER's click on and starts playback.
local METRONOME_TOGGLE, PLAY, STOP = 40364, 1007, 1016
local function setMetronome(id, on, bpm, num, den, volume)
  if not on then
    reaper.Main_OnCommand(STOP, 0)
    return status(id, true, "Metronome stopped")
  end
  bpm, num, den, volume = bpm or 100, num or 4, den or 4, volume or 40
  -- The first tempo marker (or the project tempo when there is none) sets the click.
  if reaper.CountTempoTimeSigMarkers(0) > 0 then
    reaper.SetTempoTimeSigMarker(0, 0, 0, -1, -1, bpm, num, den, false)
  else
    reaper.SetTempoTimeSigMarker(0, -1, 0, -1, -1, bpm, num, den, false)
  end
  reaper.UpdateTimeline()
  if reaper.GetToggleCommandState(METRONOME_TOGGLE) ~= 1 then reaper.Main_OnCommand(METRONOME_TOGGLE, 0) end
  if reaper.SNM_SetDoubleConfigVar then
    local gain = volume / 100
    reaper.SNM_SetDoubleConfigVar("projmetrov1", gain)      -- first beat of the bar
    reaper.SNM_SetDoubleConfigVar("projmetrov2", gain / 2)  -- other beats
  end
  if reaper.GetPlayState() & 5 == 0 then reaper.Main_OnCommand(PLAY, 0) end
  status(id, true, string.format("Metronome on, %g beats per minute, %d/%d", bpm, num, den))
end

local learn = nil -- { id, untilTime } while waiting for a hit to learn ("Learn my kit")

-- MIDI recording for diagnostics (MIDILOG request): every incoming event for a few seconds.
local logUntil, logPath, logId, logEvents = 0, nil, nil, {}

-- TD-12 backup (SYSEXREC): records the bulk dump the module sends, as a .syx file.
-- Ends 5 seconds after the last message, or at the time limit.
local sysex = nil -- { id, path, untilTime, lastTime, parts }
local function startSysexRecord(id, seconds, path)
  sysex = { id = id, path = path, untilTime = reaper.time_precise() + seconds, lastTime = nil, parts = {} }
  status(id .. ":started", true, "Waiting for the TD-12 bulk dump")
end

local function finishSysexRecord()
  if not sysex then return end
  local now = reaper.time_precise()
  local quiet = sysex.lastTime and now - sysex.lastTime > 5
  if not quiet and now < sysex.untilTime then return end
  local s = sysex
  sysex = nil
  if #s.parts == 0 then return status(s.id, false, "No data arrived from the TD-12.") end
  local f = io.open(s.path, "wb")
  if not f then return status(s.id, false, "Could not write " .. s.path) end
  f:write(table.concat(s.parts))
  f:close()
  status(s.id, true, "Received " .. #s.parts .. " messages")
end

-- TD-12 restore (SYSEXSEND): sends a .syx file one message at a time, 60 ms apart
-- (Roland asks for at least 40 ms between data messages).
local sending = nil -- { id, messages, index, output, nextTime }
local function startSysexSend(id, path, match)
  local data = readFile(path)
  if not data then return status(id, false, "Backup file not found: " .. path) end
  local output
  for i = 0, reaper.GetMaxMidiOutputs() - 1 do
    local ok, name = reaper.GetMIDIOutputName(i, "")
    if ok and name:find(match, 1, true) then output = i break end
  end
  if not output then return status(id, false, "No enabled MIDI output named " .. match) end
  local messages = {}
  for m in data:gmatch("\xF0[^\xF7]*\xF7") do messages[#messages + 1] = m end
  if #messages == 0 then return status(id, false, "The backup file has no MIDI data.") end
  sending = { id = id, messages = messages, index = 1, output = output, nextTime = 0 }
end

local function continueSysexSend()
  if not sending then return end
  local now = reaper.time_precise()
  if now < sending.nextTime then return end
  reaper.SendMIDIMessageToHardware(sending.output, sending.messages[sending.index])
  sending.index = sending.index + 1
  sending.nextTime = now + 0.06
  if sending.index > #sending.messages then
    status(sending.id, true, "Sent " .. #sending.messages .. " messages to the TD-12")
    sending = nil
  end
end

local function handleRequest()
  local req = readFile(REQUEST)
  if not req then return end
  os.remove(REQUEST)
  local r = lines(req)
  local id, cmd = r[1] or "0", r[2] or ""
  if cmd == "APPLY" then applyKit(id, r[3] or "Kit", r[4] or "")
  elseif cmd == "CAPTURE" then captureKit(id, r[3] or (dir .. "capture.txt"))
  elseif cmd == "MIDILOG" then
    logEvents, logId, logPath = {}, id, r[4] or (dir .. "midilog.txt")
    logUntil = reaper.time_precise() + (tonumber(r[3]) or 30)
    status(id, true, "Recording MIDI")
  elseif cmd == "METRO" then setMetronome(id, r[3] == "on", tonumber(r[4]), tonumber(r[5]), tonumber(r[6]), tonumber(r[7]))
  elseif cmd == "MIDITEST" then midiOutTest(id, r[3] or module("midiOut", ""))
  elseif cmd == "SYSEXREC" then startSysexRecord(id, tonumber(r[3]) or 600, r[4] or (dir .. "backup.syx"))
  elseif cmd == "SYSEXSEND" then startSysexSend(id, r[3] or "", r[4] or module("midiOut", ""))
  elseif cmd == "LEARN" then
    learn = { id = id, untilTime = reaper.time_precise() + (tonumber(r[3]) or 30) }
    status(id .. ":started", true, "Waiting for a hit")
  elseif cmd == "MIDIDEVICES" then
    local ins, outs = {}, {}
    for i = 0, 61 do
      local present, name = reaper.GetMIDIInputName(i, "")
      if present and name ~= "" then ins[#ins + 1] = name end
    end
    for i = 0, reaper.GetMaxMidiOutputs() - 1 do
      local present, name = reaper.GetMIDIOutputName(i, "")
      if present and name ~= "" then outs[#outs + 1] = name end
    end
    status(id, true, "IN=" .. table.concat(ins, "|") .. ";OUT=" .. table.concat(outs, "|"))
  elseif cmd == "MIDIRESET" then
    -- Reopens all MIDI devices; fixes the pads going silent when REAPER loses the MIDI port (for example after another program opened it).
    reaper.midi_reinit()
    status(id, true, "MIDI devices reset. Play a pad to check.")
  elseif cmd == "PADLEVELS" then
    local tr = findTarget()
    if not tr then status(id, false, "No track with EZdrummer 3 found.")
    elseif ensurePadLevels(tr) then status(id, true, "Pad levels set")
    else status(id, false, "Could not add the pad levels effect.") end
  elseif cmd == "SETUP" then
    local tr = findTarget()
    if not tr then status(id, false, "No track with EZdrummer 3 found.")
    elseif ensureRimSplit(tr) then status(id, true, "Snare rim split is on " .. trackName(tr))
    else status(id, false, "Could not add the snare rim split effect.") end
  elseif cmd == "PING" then status(id, true, "Connected to REAPER")
  else status(id, false, "Unknown request " .. cmd) end
end

-- TD-12 program changes: kit number N on the module arrives as program N-1.
local lastSeq = reaper.MIDI_GetRecentInputEvent(0) or 0

local function hex(buf)
  local t = {}
  for i = 1, #buf do t[#t + 1] = string.format("%02X", buf:byte(i)) end
  return table.concat(t, " ")
end

-- Kit buttons on the TD-12: a kit with that TD-12 kit number loads directly; otherwise, when
-- kitlist.txt exists (stepping turned on in Kit Manager), kit up/down steps through the kit list.
local KITLIST = dir .. "kitlist.txt"
local lastProgram = nil

local function stepKit(program)
  local list = readFile(KITLIST)
  if not list then return end
  local kits = {}
  for _, l in ipairs(lines(list)) do
    local path, name = l:match("^([^\t]+)\t(.*)$")
    if path then kits[#kits + 1] = { path = path, name = name } end
  end
  if #kits == 0 then return end
  -- The TD-12 has 50 kits and wraps around, so a change of 49 means one step down.
  local step = 1
  if lastProgram then
    local kits = module("kitCount", 50)
    local delta = (program - lastProgram) % kits
    if delta == 0 then return end
    if delta > kits / 2 then step = -1 end
  end
  local current = 0
  for i, k in ipairs(kits) do if k.path == currentKitPath then current = i end end
  local nextIndex
  if current == 0 then nextIndex = step > 0 and 1 or #kits
  else nextIndex = (current - 1 + step) % #kits + 1 end
  applyKit("pc" .. reaper.time_precise(), kits[nextIndex].name, kits[nextIndex].path)
end

local function handleProgram(program)
  local map = readFile(PROGRAMS)
  local handled = false
  for _, l in ipairs(lines(map or "")) do
    local num, path, name = l:match("^(%d+)\t([^\t]+)\t(.*)$")
    if num and tonumber(num) == program + 1 then
      applyKit("pc" .. reaper.time_precise(), name, path)
      handled = true
      break
    end
  end
  if not handled then stepKit(program) end
  lastProgram = program
end

local function handleProgramChange()
  local idx, program, newest = 0, nil, nil
  local newSysex = {}
  while idx < 256 do
    local seq, buf, ts, dev = reaper.MIDI_GetRecentInputEvent(idx)
    if not seq or seq == 0 or seq <= lastSeq then break end
    newest = newest or seq
    if logPath then
      local _, devName = reaper.GetMIDIInputName(dev or 0, "")
      logEvents[#logEvents + 1] = { seq, string.format("%.4f\t%s\t%s",reaper.time_precise(), hex(buf), devName or "") }
    end
    if sysex and buf:byte(1) == 0xF0 then newSysex[#newSysex + 1] = buf end
    -- Only kit changes on channel 10 (the TD-12's drum channel). The TD-12's patterns send
    -- program changes for their backing parts on other channels, which must not reload the kit.
    local channel = module("drumChannel", 10)
    if not program and #buf >= 2 and (buf:byte(1) & 0xF0) == 0xC0
       and (channel == 0 or (buf:byte(1) & 0x0F) == channel - 1) then program = buf:byte(2) end
    -- "Learn my kit": the first hit after the request is the pad being learned.
    if learn and #buf >= 3 and (buf:byte(1) & 0xF0) == 0x90 and buf:byte(3) > 0 then
      status(learn.id, true, string.format("%d %d %d", buf:byte(2), (buf:byte(1) & 0x0F) + 1, buf:byte(3)))
      learn = nil
    end
    idx = idx + 1
  end
  if newest then lastSeq = newest end
  -- Events come newest first; keep the backup in the order the TD-12 sent it.
  if sysex and #newSysex > 0 then
    for i = #newSysex, 1, -1 do sysex.parts[#sysex.parts + 1] = newSysex[i] end
    sysex.lastTime = reaper.time_precise()
  end
  if program then handleProgram(program) end
end

local function finishLog()
  if not logPath or reaper.time_precise() < logUntil then return end
  table.sort(logEvents, function(x, y) return x[1] < y[1] end)
  local out = {}
  for _, e in ipairs(logEvents) do out[#out + 1] = e[2] end
  writeFile(logPath, table.concat(out, "\n"))
  status(logId, true, "MIDI log saved, " .. #out .. " events")
  logPath = nil
end

local nextCheck = 0
local function loop()
  handleProgramChange()
  finishLog()
  finishSysexRecord()
  if learn and reaper.time_precise() > learn.untilTime then
    status(learn.id, false, "No hit arrived")
    learn = nil
  end
  continueSysexSend()
  local now = reaper.time_precise()
  if now >= nextCheck then
    nextCheck = now + 0.25
    handleRequest()
  end
  reaper.defer(loop)
end

reaper.RecursiveCreateDirectory(dir, 0)
loop()
