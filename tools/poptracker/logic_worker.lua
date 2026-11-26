-- Runs in a PopTracker async thread (ScriptHost:RunScriptAsync), outside the time limit that
-- applies to the main Lua state. arg = { slot = slot_data or nil, hk = {items}, ss = {items} }.
-- Returns { reachable = {location ids}, overrides = decoded Silksong overrides JSON or nil }.

require "data_names"
require "data_hk"
require "logic_hk"
require "data_ss"
require "logic_ss"
require "codec"
require "data_seals"

local slot = arg.slot or {}
local result = { reachable = {} }

-- Cross-game seals whose key (an item from the other game) has not been received:
-- Hollow Knight scenes and Silksong area ids that stay out of logic.
local function closed_seals()
    local hk_scenes, ss_areas = {}, {}
    for _, seal in ipairs(slot.cross_game_seals or {}) do
        local key = seal.key or ""
        local inventory = key:sub(1, 3) == "HK " and arg.hk or arg.ss
        if ((inventory or {})[key:sub(4)] or 0) < 1 then
            if seal.game == "hollow_knight" then
                for _, scene in ipairs(seal.scenes or {}) do hk_scenes[scene] = true end
            elseif seal.area_id then
                ss_areas[seal.area_id] = true
            end
        end
    end
    return hk_scenes, ss_areas
end

local ok, err = pcall(function()
    local hk_sealed, ss_sealed = closed_seals()
    local hk = HK.evaluate(arg.hk or {}, slot.hollow_knight, hk_sealed)
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
    local ss = SS.evaluate(arg.ss or {}, silksong, SS.payload(silksong), ss_sealed)
    local can_reach_pharloom = (slot.hollow_knight == nil) or (hk:count("Town") >= 1)
    for id, name in pairs(LOCATION_NAMES) do
        local reach
        if name:sub(1, 3) == "HK " then
            reach = hk:can_reach(name:sub(4))
        elseif not can_reach_pharloom then
            reach = false
        else
            reach = ss:can_reach(name:sub(4))
        end
        if reach ~= false then result.reachable[#result.reachable + 1] = id end
    end
    if SS.mismatch then result.mismatch = true end
end)
if not ok then result.error = tostring(err) end
return result
