-- Archipelago auto-tracking: items received and locations checked in this slot.

CUR_INDEX = -1
CHECKED_LOCATIONS = {}

local function reset_items()
    for _, entry in pairs(ITEM_MAPPING) do
        local object = Tracker:FindObjectForCode(entry[1])
        if object then
            if entry[2] == "toggle" then
                object.Active = false
            else
                object.AcquiredCount = 0
            end
        end
    end
end

local function reset_locations()
    for _, entry in pairs(LOCATION_MAPPING) do
        local object = Tracker:FindObjectForCode(entry[1])
        if object then
            object.AvailableChestCount = object.ChestCount
        end
    end
end

-- Checks that are not in this slot (excluded by its options) are shown as done.
local function clear_absent_locations()
    if not Archipelago.MissingLocations or not Archipelago.CheckedLocations then
        return
    end
    local present = {}
    for _, id in ipairs(Archipelago.MissingLocations) do
        present[id] = true
    end
    for _, id in ipairs(Archipelago.CheckedLocations) do
        present[id] = true
    end
    for id, entry in pairs(LOCATION_MAPPING) do
        if not present[id] then
            local object = Tracker:FindObjectForCode(entry[1])
            if object then
                object.AvailableChestCount = 0
            end
        end
    end
end

-- Logic is re-evaluated on the next frame (see logic.lua).
local function touch_logic()
    LOGIC.changed()
end

function onClear(slot_data)
    CUR_INDEX = -1
    CHECKED_LOCATIONS = {}
    LOGIC.received = {}
    LOGIC.set_slot(slot_data)
    reset_locations()
    reset_items()
    clear_absent_locations()
    touch_logic()
end

function onItem(index, item_id, item_name, player_number)
    if index <= CUR_INDEX then
        return
    end
    CUR_INDEX = index
    local name = ITEM_NAMES[item_id] or item_name
    if name then
        LOGIC.received[name] = (LOGIC.received[name] or 0) + 1
    end
    local entry = ITEM_MAPPING[item_id]
    local object = entry and Tracker:FindObjectForCode(entry[1])
    if object then
        if entry[2] == "toggle" then
            object.Active = true
        elseif object.AcquiredCount < object.MaxCount then
            object.AcquiredCount = object.AcquiredCount + object.Increment
        end
    end
    touch_logic()
end

function onLocation(location_id, location_name)
    if CHECKED_LOCATIONS[location_id] then
        return
    end
    CHECKED_LOCATIONS[location_id] = true
    local entry = LOCATION_MAPPING[location_id]
    if not entry then
        return
    end
    local object = Tracker:FindObjectForCode(entry[1])
    if object and object.AvailableChestCount > 0 then
        object.AvailableChestCount = object.AvailableChestCount - 1
    end
end

Archipelago:AddClearHandler("clear handler", onClear)
Archipelago:AddItemHandler("item handler", onItem)
Archipelago:AddLocationHandler("location handler", onLocation)
