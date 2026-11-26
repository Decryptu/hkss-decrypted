-- Ties the logic engines to PopTracker: every check section has the access rule
-- "$hkss_reach|<location id>". Evaluating the logic is too much work for the main Lua state
-- (PopTracker limits how long each call may run), so it runs in an async thread
-- (logic_worker.lua) whenever items or the slot changed, and the rules read its answers.
-- Items come from Archipelago (received items) and from the item grid (manual tracking), and
-- the slot's options and logic arrive with the slot data when PopTracker connects.

LOGIC = {
    slot = nil,         -- slot_data of the connected slot
    received = {},      -- [Archipelago item name] = count
    dirty = true,
    running = false,
    reachable = nil,    -- [location id] = true, from the last finished evaluation
}

function LOGIC.changed()
    LOGIC.dirty = true
end

-- The item grid counts too, so the pack works without a connection.
local function grid_count(code)
    local object = Tracker:FindObjectForCode(code)
    if not object then return 0 end
    if object.Type == "toggle" then return object.Active and 1 or 0 end
    return object.AcquiredCount or 0
end

local function inventory(prefix)
    local items = {}
    for name, count in pairs(LOGIC.received) do
        if name:sub(1, 3) == prefix then
            items[name:sub(4)] = (items[name:sub(4)] or 0) + count
        elseif CROSS_GAME_ITEMS and CROSS_GAME_ITEMS[name] then
            local mapped = prefix == "HK " and CROSS_GAME_ITEMS[name].hk or CROSS_GAME_ITEMS[name].ss
            if mapped then items[mapped] = (items[mapped] or 0) + count end
        end
    end
    for code, name in pairs(GRID_ITEMS) do
        if name:sub(1, 3) == prefix then
            local key, count = name:sub(4), grid_count(code)
            if count > (items[key] or 0) then items[key] = count end
        end
    end
    return items
end

function LOGIC.set_slot(slot_data)
    LOGIC.slot = slot_data
    LOGIC.changed()
end

local function refresh_rules()
    local tick = Tracker:FindObjectForCode("hkss_logic")
    if tick then tick.AcquiredCount = (tick.AcquiredCount + 1) % 100000 end
end

local function finished(result)
    LOGIC.running = false
    if type(result) ~= "table" then
        print("HKSS logic: no result from the logic thread")
        return
    end
    if result.error then print("HKSS logic error: " .. result.error) end
    if result.mismatch then
        print("HKSS: this pack does not match the seed's Silksong logic. Use the pack from the same release.")
    end
    local silksong = LOGIC.slot and LOGIC.slot.silksong
    if result.overrides and silksong then silksong.logic_overrides_json = result.overrides end
    local reachable, count = {}, 0
    for _, id in ipairs(result.reachable or {}) do
        reachable[id] = true
        count = count + 1
    end
    LOGIC.reachable = reachable
    print("HKSS logic: " .. count .. " checks in logic")
    refresh_rules()
end

local function start()
    LOGIC.dirty = false
    LOGIC.running = true
    local arg = { slot = LOGIC.slot, hk = inventory("HK "), ss = inventory("SS ") }
    ScriptHost:RunScriptAsync("scripts/logic_worker.lua", arg, finished)
end

-- Access rule: true when the check is in logic. Until the first evaluation finishes, and for
-- checks without a rule, the check counts as reachable.
function hkss_reach(id)
    if not LOGIC.reachable then return true end
    return LOGIC.reachable[tonumber(id)] == true
end

ScriptHost:AddOnFrameHandler("hkss logic", function()
    if LOGIC.dirty and not LOGIC.running then start() end
end)

-- Re-evaluate when the item grid changes (manual tracking), but not for the refresh counter.
ScriptHost:AddWatchForCode("hkss logic", "*", function(code)
    if code ~= "hkss_logic" then LOGIC.changed() end
end)
