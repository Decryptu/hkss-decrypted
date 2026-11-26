-- Silksong reachability for the HKSS PopTracker pack.
-- The world ships one base map logic (SS_LOGIC.base, the Silksong world's map_logic.json) and
-- sends each slot's differences in slot_data (logic_base / logic_overrides). This file rebuilds
-- the slot's logic the way map_logic_data.restore does, then evaluates it from received items:
-- abstract requirements (room nodes, room events, capabilities, options) and logic events are
-- resolved to a fixed point, and a location is reachable when one of its alternatives holds.

SS = {}

local DEFAULTS = {
    all_of = {}, any_of = {}, required_locations = {}, item_counts = {},
    require_any_crest = false, require_silk_spear = false, minimum_skip_tier = 0,
    act = "", path = "",
}

local function copy(value)
    if type(value) ~= "table" then return value end
    local result = {}
    for k, v in pairs(value) do result[k] = copy(v) end
    return result
end

local function equal(a, b)
    if type(a) ~= type(b) then return false end
    if type(a) ~= "table" then return a == b end
    for k, v in pairs(a) do
        if not equal(v, b[k]) then return false end
    end
    for k in pairs(b) do
        if a[k] == nil then return false end
    end
    return true
end

local function contains(list, value)
    for _, item in ipairs(list) do
        if equal(item, value) then return true end
    end
    return false
end

-- map_logic_data._merge_changes. JSON nulls (removals) do not survive the trip into Lua, so a
-- removal is only honoured when the slot data keeps it as the SS.NULL sentinel.
local function merge(result, changes)
    for key, value in pairs(changes) do
        if value == SS.NULL then
            result[key] = nil
        elseif type(value) == "table" and next(value) ~= nil and value[1] == nil then
            local target = result[key]
            if type(target) ~= "table" then target = {} end
            result[key] = merge(target, value)
        else
            result[key] = copy(value)
        end
    end
    return result
end
SS.NULL = {}

-- map_logic_data.prepare_base
local function prepare_base(slot)
    local result = copy(SS_LOGIC.base)
    if (slot.entrance_randomization or "off") == "coupled" then
        local profile = SS_LOGIC.profiles[slot.entrance_randomization_scope or "full"]
        result = merge(result, profile.changes)
        local pairs_ = slot.entrance_pairs or {}
        for _, source in ipairs(profile.sources) do
            local destination = profile.destinations[pairs_[source.id] or source.vanilla]
            local group = result.abstract_requirements[destination]
            if group == nil then
                group = { alternatives = {} }
                result.abstract_requirements[destination] = group
            end
            for _, requirement in ipairs(source.requirements) do
                if not contains(group.alternatives, requirement) then
                    group.alternatives[#group.alternatives + 1] = copy(requirement)
                end
            end
        end
    end
    if not slot.scuttlebrace_logic or slot.scuttlebrace_logic == 0 then
        result.abstract_requirements["Usable Scuttlebrace"] = nil
        local groups = {}
        for _, g in pairs(result.requirements) do groups[#groups + 1] = g end
        for _, g in pairs(result.abstract_requirements) do groups[#groups + 1] = g end
        for _, e in pairs(result.logic_events) do groups[#groups + 1] = e.requirement end
        for _, group in ipairs(groups) do
            local alternatives = {}
            for _, requirement in ipairs(group.alternatives) do
                local keep = true
                for _, name in ipairs(requirement.all_of or {}) do
                    if name == "Usable Scuttlebrace" then keep = false break end
                end
                if keep and requirement.any_of and #requirement.any_of > 0 then
                    local filtered = {}
                    for _, name in ipairs(requirement.any_of) do
                        if name ~= "Usable Scuttlebrace" then filtered[#filtered + 1] = name end
                    end
                    if #filtered == 0 then keep = false else requirement.any_of = filtered end
                end
                if keep then alternatives[#alternatives + 1] = requirement end
            end
            group.alternatives = alternatives
        end
    end
    return result
end

local function decode_overrides(changes)
    if type(changes) == "table" and changes.encoding then
        if changes.encoding ~= "gzip+base64" or not SS.gunzip then return nil end
        local raw = SS.gunzip(SS.base64(changes.data))
        if not raw then return nil end
        return SS.json(raw)
    end
    return changes
end

-- Rebuilds a slot's logic payload (map_logic_data.restore). Without slot data, the base logic.
function SS.payload(slot)
    if not slot then return SS.compile(copy(SS_LOGIC.base)) end
    if slot.logic_base and slot.logic_base ~= SS_LOGIC.identity then
        SS.mismatch = true -- the pack does not match this seed's world
    end
    local payload = prepare_base(slot)
    local changes = decode_overrides(slot.logic_overrides)
    if changes then merge(payload, changes) end
    payload.vanilla_sources = slot.tracker_vanilla_sources
    return SS.compile(payload)
end

local CRESTS = SS_LOGIC_CONSTANTS.crests
local NON_ARCHITECT = SS_LOGIC_CONSTANTS.non_architect_crests
local CLOAK_REQUIRED = {}
for _, name in ipairs(SS_LOGIC_CONSTANTS.cloak_required) do CLOAK_REQUIRED[name] = true end
local CLEAN = SS_LOGIC_CONSTANTS.clean_names

local function clean(name)
    return CLEAN[name] or name
end

-- Precomputes evaluation order and dependency indexes for a payload.
function SS.compile(payload)
    local compiled = {
        requirements = payload.requirements,
        abstract = payload.abstract_requirements,
        dependencies = payload.logic_item_dependencies or {},
        rules = {},      -- index -> requirement group
        names = {},      -- index -> abstract name (or nil for events)
        events = {},     -- index -> logic event
        abstract_users = {},
        item_users = {},
    }
    local function add_user(map, name, index)
        local users = map[name]
        if not users then users = {} map[name] = users end
        users[#users + 1] = index
    end
    local order = {}
    for name in pairs(payload.abstract_requirements) do order[#order + 1] = name end
    table.sort(order)
    for _, name in ipairs(order) do
        compiled.rules[#compiled.rules + 1] = payload.abstract_requirements[name]
        compiled.names[#compiled.rules] = name
    end
    local event_order = payload.logic_event_order or {}
    if #event_order == 0 then
        for name in pairs(payload.logic_events or {}) do event_order[#event_order + 1] = name end
        table.sort(event_order)
    end
    -- With the slot's vanilla source list, native pickups are counted from that list instead.
    local sources = payload.vanilla_sources
    for _, name in ipairs(event_order) do
        local event = payload.logic_events[name]
        if event and event.item and event.requirement and not (sources and event.native_source) then
            compiled.rules[#compiled.rules + 1] = event.requirement
            compiled.events[#compiled.rules] = event
        end
    end

    -- Vanilla pickups the world collects as events once their location is reachable.
    for _, source in ipairs(sources or {}) do
        local requirement = payload.requirements[source[1]]
        if requirement then
            compiled.rules[#compiled.rules + 1] = requirement
            compiled.events[#compiled.rules] = { item = source[2] }
        end
    end
    -- The Goal location holds the Victory event, which logic-unknown checks require.
    if payload.requirements.Goal then
        compiled.rules[#compiled.rules + 1] = payload.requirements.Goal
        compiled.events[#compiled.rules] = { item = "Victory" }
    end

    for index, group in ipairs(compiled.rules) do
        local visited = {}
        local function named(name)
            if compiled.abstract[name] then add_user(compiled.abstract_users, name, index) return end
            local item = clean(name)
            add_user(compiled.item_users, item, index)
            add_user(compiled.item_users, "Progressive Swift Step", index)
            for _, dependency in ipairs(compiled.dependencies[item] or {}) do
                add_user(compiled.item_users, clean(dependency), index)
            end
            if CLOAK_REQUIRED[item] then
                for _, crest in ipairs(CRESTS) do add_user(compiled.item_users, crest, index) end
            end
        end
        local function visit(rule)
            if rule.require_any_crest or rule.require_silk_spear then
                for _, crest in ipairs(CRESTS) do add_user(compiled.item_users, crest, index) end
            end
            if rule.require_silk_spear then add_user(compiled.item_users, "Silkspear", index) end
            for _, name in ipairs(rule.all_of or {}) do named(name) end
            for _, name in ipairs(rule.any_of or {}) do named(name) end
            for _, count in ipairs(rule.item_counts or {}) do
                for _, name in ipairs(count.items or {}) do add_user(compiled.item_users, clean(name), index) end
            end
            for _, location in ipairs(rule.required_locations or {}) do
                if not visited[location] and compiled.requirements[location] then
                    visited[location] = true
                    visit(compiled.requirements[location])
                end
            end
            for _, alternative in ipairs(rule.alternatives or {}) do visit(alternative) end
        end
        visit(group)
    end
    return compiled
end

local State = {}
State.__index = State

function State:count(name)
    return self.inventory[clean(name)] or 0
end

function State:has_any(list)
    for _, name in ipairs(list) do
        if self:count(name) > 0 then return true end
    end
    return false
end

-- _compile_named_requirement: an abstract requirement, or an item with its dependencies.
function State:named(name)
    local value = self.values[name]
    if value ~= nil then return value end
    if self.logic.abstract[name] then return false end
    local item = clean(name)
    if self.split_dash_and_sprint and item == "Swift Step" then
        return self:count("Progressive Swift Step") >= 2
    end
    if self:count(item) < 1 then return false end
    if CLOAK_REQUIRED[item] and not self:has_any(CRESTS) then return false end
    local dependencies = self.logic.dependencies[item]
    if dependencies then
        local needed = {}
        for _, dependency in ipairs(dependencies) do
            local key = clean(dependency)
            needed[key] = (needed[key] or 0) + 1
        end
        for key, amount in pairs(needed) do
            if self:count(key) < amount then return false end
        end
    end
    return true
end

local function at_least(state, name, minimum)
    return state:count(name) >= math.max(0, minimum or 0)
end

-- An item_counts entry, split by direction like directional_abilities.split_requirement_counts.
function State:item_count(count)
    local items, minimum = count.items or {}, count.minimum or 0
    local enabled = self.directional
    if #items == 1 then
        local name = clean(items[1])
        local ability = (name == "Swift Step" and enabled.Dash) and "Dash" or name
        if enabled[ability] then
            return at_least(self, ability .. " (Left)", minimum) and at_least(self, ability .. " (Right)", minimum)
        elseif name == "Progressive Swift Step" and enabled.Dash then
            if not at_least(self, "Sprint", math.min(minimum, 1)) then return false end
            if minimum >= 2 then
                return at_least(self, "Dash (Left)", 1) and at_least(self, "Dash (Right)", 1)
            end
            return true
        end
    end
    local total = 0
    for _, name in ipairs(items) do total = total + self:count(name) end
    return total >= math.max(0, minimum)
end

-- directional_abilities.selected
local function directional(slot)
    local function on(name) return slot[name] == true or (type(slot[name]) == "number" and slot[name] ~= 0) end
    local enabled = {}
    if on("ledgegrab_ability_rando") and on("directional_ledge_grab") then enabled["Ledge Grab"] = true end
    if slot.skill_randomization == "anywhere" then
        if on("directional_cling_grip") then enabled["Cling Grip"] = true end
        if on("directional_clawline") then enabled["Clawline"] = true end
        if on("directional_swift_step") then
            enabled[on("split_dash_and_sprint") and "Dash" or "Swift Step"] = true
        end
    end
    return enabled
end

function State:requirement(rule, active)
    if (rule.minimum_skip_tier or 0) > self.skips_tier then return false end
    if rule.require_any_crest and not self:has_any(CRESTS) then return false end
    if rule.require_silk_spear and not (self:count("Silkspear") > 0 and self:has_any(NON_ARCHITECT)) then
        return false
    end
    for _, name in ipairs(rule.all_of or {}) do
        if not self:named(name) then return false end
    end
    local any_of = rule.any_of or {}
    local minimum = rule.minimum_any_of or 1
    if #any_of > 0 or minimum ~= 1 then
        local found, seen = 0, {}
        for _, name in ipairs(any_of) do
            if minimum == 1 or not seen[name] then
                seen[name] = true
                if self:named(name) then
                    found = found + 1
                    if found >= minimum then break end
                end
            end
        end
        if found < minimum then return false end
    end
    for _, count in ipairs(rule.item_counts or {}) do
        if not self:item_count(count) then return false end
    end
    for _, location in ipairs(rule.required_locations or {}) do
        if not self:location(location, active) then return false end
    end
    return true
end

-- Logic-unknown checks are evaluated as the slot data gives them (behind Victory).
function State:group(group, active)
    if group == nil then return false end
    for _, alternative in ipairs(group.alternatives or {}) do
        if self:requirement(alternative, active) then return true end
    end
    return false
end

-- A required location with unknown logic never counts (requirement_rules.compile_required_location).
function State:location(name, active)
    local group = self.logic.requirements[name]
    if group == nil or group.logic_unknown then return false end
    active = active or {}
    if active[name] then return false end
    active[name] = true
    local result = self:group(group, active)
    active[name] = nil
    return result
end

-- True for room nodes and room events in a cross-game sealed area that is still closed.
local function in_sealed_area(name, sealed)
    if name == nil or next(sealed) == nil then return false end
    local area = name:match("^Room Node: ([^/]+)/")
    if area then return sealed[area] == true end
    area = SEAL_DATA and SEAL_DATA.ssEventAreas[name]
    return area ~= nil and sealed[area] == true
end

-- items: {[item name without "SS "] = count}; slot: slot_data.silksong or nil;
-- logic: SS.payload(slot), cached by the caller; sealed: {[area id] = true} or nil.
function SS.evaluate(items, slot, logic, sealed)
    slot = slot or {}
    sealed = sealed or {}
    local inventory = {}
    local enabled = directional(slot)
    -- With split directional dash, each Progressive Swift Step grants the next of Sprint and the
    -- two dash sides (SilksongWorld.collect_item / directional_abilities.progressive_steps).
    local steps = slot.swift_step_first_right and { "Sprint", "Dash (Right)", "Dash (Left)" }
        or { "Sprint", "Dash (Left)", "Dash (Right)" }
    for name, count in pairs(items) do
        local key = clean(name)
        if key == "Progressive Swift Step" and enabled.Dash then
            for _ = 1, count do
                for _, step in ipairs(steps) do
                    if (inventory[step] or 0) == 0 then inventory[step] = 1 break end
                end
            end
        else
            inventory[key] = (inventory[key] or 0) + count
        end
    end
    -- The world gives the Diving Bell Key from the start unless it is randomized.
    if slot.diving_bell_key_randomization ~= nil and not slot.diving_bell_key_randomization
        or slot.diving_bell_key_randomization == 0 then
        inventory["Diving Bell Key"] = math.max(inventory["Diving Bell Key"] or 0, 1)
    end
    local state = setmetatable({
        logic = logic,
        inventory = inventory,
        values = {},
        skips_tier = slot.skips_tier or 0,
        split_dash_and_sprint = (slot.split_dash_and_sprint == true or slot.split_dash_and_sprint == 1)
            and slot.skill_randomization == "anywhere",
        directional = enabled,
    }, State)
    for _, name in pairs(logic.names) do state.values[name] = false end

    local rules, names, events = logic.rules, logic.names, logic.events
    local completed, queued, pending, head = {}, {}, {}, 1
    for index = 1, #rules do
        pending[#pending + 1] = index
        queued[index] = true
    end
    local function notify(users)
        if not users then return end
        for _, index in ipairs(users) do
            if not completed[index] and not queued[index] then
                queued[index] = true
                pending[#pending + 1] = index
            end
        end
    end
    while head <= #pending do
        local index = pending[head]
        head = head + 1
        queued[index] = false
        if not completed[index] and not in_sealed_area(names[index], sealed)
            and state:group(rules[index]) then
            completed[index] = true
            local event = events[index]
            if event then
                local item = clean(event.item)
                inventory[item] = (inventory[item] or 0) + 1
                notify(logic.item_users[item])
            else
                state.values[names[index]] = true
                notify(logic.abstract_users[names[index]])
            end
        end
    end
    return state
end

-- name: Archipelago location name without the "SS " prefix.
function State:can_reach(name)
    local group = self.logic.requirements[name]
    if group == nil then return nil end
    return self:group(group, { [name] = true })
end
