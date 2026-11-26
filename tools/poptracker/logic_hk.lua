-- Hollow Knight reachability for the HKSS PopTracker pack.
-- A port of HKSS.Core/HkLogicEvaluator.cs over the same rules (HK_LOGIC, exported from the
-- Archipelago Hollow Knight world). Rules are nested tables: a string is a term that must be
-- held, {"&", ...} and {"|", ...} combine, {"gt", term, n} needs more than n, {"opt", name} and
-- {"opt0", name} test slot options, {"notches", {charm indices}} needs enough charm notches.

HK = {}

local function strip_number(name)
    local base, number = name:match("^(.*)_(%d+)$")
    if base then return base end
    return nil
end

local State = {}
State.__index = State

function State:count(term)
    local c = self.counts[term]
    if c and c > 0 then return c end
    return self.reached[term] and 1 or 0
end

function State:option(name)
    return self.options[name] or 0
end

function State:notch_sum(indices)
    local sum = 0
    for _, index in ipairs(indices) do
        local cost = self.notch_costs[index + 1]
        sum = sum + (cost or 2)
    end
    return sum
end

function State:eval(expr)
    local kind = type(expr)
    if kind == "string" then return self:count(expr) >= 1 end
    if kind == "boolean" then return expr end
    local op = expr[1]
    if op == "&" then
        for i = 2, #expr do
            if not self:eval(expr[i]) then return false end
        end
        return true
    elseif op == "|" then
        for i = 2, #expr do
            if self:eval(expr[i]) then return true end
        end
        return false
    elseif op == "gt" then
        return self:count(expr[2]) > expr[3]
    elseif op == "opt" then
        return self:option(expr[2]) ~= 0
    elseif op == "opt0" then
        return self:option(expr[2]) == 0
    elseif op == "notches" then
        return self:count("NOTCHES") > self:notch_sum(expr[2])
    end
    return false
end

local function add(counts, key, delta)
    counts[key] = (counts[key] or 0) + delta
end

local function apply_item(counts, name)
    add(counts, name, 1)
    local effects = HK_LOGIC.itemEffects[name]
    if effects then
        for term, delta in pairs(effects) do add(counts, term, delta) end
    end
    -- Split Mothwing Cloak + Shade Cloak sync (matches HKWorld.collect_effects).
    if name == "Left_Mothwing_Cloak" or name == "Right_Mothwing_Cloak" then
        local left, right = counts.LEFTDASH or 0, counts.RIGHTDASH or 0
        if left > 0 and right > 0 then
            local most = math.max(left, right)
            counts.LEFTDASH, counts.RIGHTDASH = most, most
        end
    end
end

-- items: {[clean item name] = count}; slot: slot_data.hollow_knight or nil.
function HK.evaluate(items, slot)
    slot = slot or {}
    -- Before a slot is connected, assume the world's default options.
    local options = slot.options or HK_LOGIC.defaultOptions or {}
    -- The world grants the unrandomized starting abilities as start items, so a connected slot
    -- receives them like any other item. Without a slot, assume the default ones.
    local counts = {}
    if not slot.options then
        for _, name in ipairs({ "Focus", "Swim", "Upslash", "Leftslash", "Rightslash", "Downslash" }) do
            if not items[name] then apply_item(counts, name) end
        end
    end
    -- Apply items in a fixed order so the cloak sync behaves the same every time.
    local names = {}
    for name in pairs(items) do names[#names + 1] = name end
    table.sort(names)
    for _, name in ipairs(names) do
        for _ = 1, items[name] do apply_item(counts, name) end
    end

    local connectors = HK_LOGIC.connectors
    if slot.entrance_pairs and next(slot.entrance_pairs) then
        local merged = {}
        for k, v in pairs(connectors) do merged[k] = v end
        for k, v in pairs(slot.entrance_pairs) do merged[k] = v end
        connectors = merged
    end

    local state = setmetatable({
        counts = counts,
        options = options,
        notch_costs = slot.notch_costs or {},
        location_costs = slot.location_costs or {},
        reached = { [HK_LOGIC.start] = true },
    }, State)

    HK.vanilla_shade_cloak = slot.vanilla_shade_cloak
    local events = HK.vanilla_events(options)
    local order, rules, reached = HK_LOGIC.nodeOrder, HK_LOGIC.nodeRules, state.reached
    local changed = true
    while changed do
        changed = false
        for _, node in ipairs(order) do
            if not reached[node] and state:eval(rules[node]) then
                reached[node] = true
                changed = true
                local target = connectors[node]
                if target and target ~= "" and not reached[target] then reached[target] = true end
            end
        end
        -- Vanilla placements the world turned into events: collect each once it is reachable.
        for _, event in ipairs(events) do
            if not event.done and state:reach_rule(event.location, event.costs) then
                event.done = true
                apply_item(counts, event.item)
                changed = true
            end
        end
    end
    return state
end

-- The vanilla item/location pairs the world places as events for this slot's options
-- (HKWorld.create_items: unrandomized options, split abilities, White Palace exclusions).
function HK.vanilla_events(options)
    local function on(name) return (options[name] or 0) ~= 0 end
    local events = {}
    if on("AddUnshuffledLocations") then return events end -- those become real checks
    local excluded = {}
    local wp_key = tostring(options.WhitePalace or 0) .. "_" .. (on("RandomizeCharms") and "1" or "0")
    for _, name in ipairs(HK_LOGIC.whitePalace[wp_key] or {}) do excluded[name] = true end
    local logicless = {}
    for _, name in ipairs(HK_LOGIC.logicless) do logicless[name] = true end
    local junk = {}
    if on("RemoveSpellUpgrades") then
        junk.Abyss_Shriek, junk.Shade_Soul, junk.Descending_Dark = true, true, true
    end
    local shop_costs = {}
    for key, list in pairs(HK_LOGIC.shopCosts) do
        local copy = {}
        for i, cost in ipairs(list) do copy[i] = cost end
        shop_costs[key] = copy
    end

    local function add_event(item, location, randomized)
        local vanilla = not randomized
        if randomized and excluded[location] and location ~= "King_Fragment" then vanilla = true end
        if not vanilla or junk[item] then return end
        -- create_vanilla_location takes the last listed vanilla cost for repeated shop items.
        local costs = nil
        local list = shop_costs[location .. "|" .. item]
        if list and #list > 0 then
            costs = table.remove(list)
        elseif HK_LOGIC.locationCosts[location] then
            costs = HK_LOGIC.locationCosts[location]
        end
        events[#events + 1] = { item = item, location = location, costs = costs }
    end

    for _, pair in ipairs(HK_LOGIC.vanilla) do
        local randomized = on(pair.o)
        if randomized or not logicless[pair.o] then
            local item, location = pair.i, pair.l
            if (item == "Crystal_Heart" and on("SplitCrystalHeart"))
                or (item == "Mothwing_Cloak" and on("SplitMothwingCloak")) then
                add_event("Left_" .. item, location, randomized)
                add_event("Right_" .. item, "Split_" .. location, randomized)
            elseif item == "Mantis_Claw" and on("SplitMantisClaw") then
                add_event("Left_" .. item, "Left_" .. location, randomized)
                add_event("Right_" .. item, "Right_" .. location, randomized)
            elseif item == "Shade_Cloak" and on("SplitMothwingCloak") then
                -- The world picks a random side here and records it in slot data. Older seeds
                -- do not say which, so neither side is credited until the item itself arrives.
                if HK.vanilla_shade_cloak then add_event(HK.vanilla_shade_cloak, location, randomized) end
            elseif item == "Grimmchild2" and on("RandomizeGrimmkinFlames") and on("RandomizeCharms") then
                add_event("Grimmchild1", location, randomized)
            else
                add_event(item, location, randomized)
            end
        end
    end
    return events
end

local function find_rule(name)
    local rule = HK_LOGIC.locationRules[name]
    if rule == nil then
        local base = strip_number(name)
        if base then rule = HK_LOGIC.locationRules[base] end
    end
    if rule == nil then rule = HK_LOGIC.nodeRules[name] end
    return rule
end

-- True when the location's rule holds and its non-Geo costs are met (there is no Geo logic).
function State:reach_rule(name, costs)
    local rule = find_rule(name)
    if rule == nil or not self:eval(rule) then return false end
    if costs then
        for term, amount in pairs(costs) do
            if term:upper() ~= "GEO" and self:count(term) < amount then return false end
        end
    end
    return true
end

-- name: Archipelago location name without the "HK " prefix. Nil when there is no rule.
function State:can_reach(name)
    if find_rule(name) == nil then return nil end
    return self:reach_rule(name, self.location_costs[name] or HK_LOGIC.locationCosts[name])
end
