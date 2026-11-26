-- Runs in a PopTracker async thread (ScriptHost:RunScriptAsync), outside the time limit that
-- applies to the main Lua state. arg = { slot = slot_data or nil, hk = {items}, ss = {items} }.
-- Returns { reachable = {location ids}, overrides = decoded Silksong overrides JSON or nil }.

require "data_names"
require "data_hk"
require "logic_hk"
require "data_ss"
require "logic_ss"
require "codec"

local slot = arg.slot or {}
local result = { reachable = {} }

local ok, err = pcall(function()
    local hk = HK.evaluate(arg.hk or {}, slot.hollow_knight)
    local silksong = slot.silksong
    if silksong and silksong.logic_overrides_json then
        -- Overrides decoded by an earlier run; skip the slow inflate.
        silksong.logic_overrides = SS.json(silksong.logic_overrides_json)
    elseif silksong and type(silksong.logic_overrides) == "table" and silksong.logic_overrides.encoding then
        local raw = SS.gunzip(SS.base64(silksong.logic_overrides.data))
        if raw then
            result.overrides = raw
            silksong.logic_overrides = SS.json(raw)
        end
    end
    local ss = SS.evaluate(arg.ss or {}, silksong, SS.payload(silksong))
    for id, name in pairs(LOCATION_NAMES) do
        local reach
        if name:sub(1, 3) == "HK " then
            reach = hk:can_reach(name:sub(4))
        else
            reach = ss:can_reach(name:sub(4))
        end
        if reach ~= false then result.reachable[#result.reachable + 1] = id end
    end
    if SS.mismatch then result.mismatch = true end
end)
if not ok then result.error = tostring(err) end
return result
