-- Small decoders for compressed Silksong logic overrides (slot_data "gzip+base64"):
-- base64, gzip/DEFLATE (RFC 1951/1952) and JSON. PopTracker's Lua has no zlib or JSON library.

local B64 = {}
do
    local alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
    for i = 1, #alphabet do B64[alphabet:byte(i)] = i - 1 end
end

function SS.base64(text)
    local out, bits, value = {}, 0, 0
    for i = 1, #text do
        local digit = B64[text:byte(i)]
        if digit then
            value = (value << 6) | digit
            bits = bits + 6
            if bits >= 8 then
                bits = bits - 8
                out[#out + 1] = string.char((value >> bits) & 0xFF)
            end
        end
    end
    return table.concat(out)
end

-- DEFLATE --------------------------------------------------------------------------------

local LENGTH_BASE = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59,
    67, 83, 99, 115, 131, 163, 195, 227, 258 }
local LENGTH_EXTRA = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4,
    5, 5, 5, 5, 0 }
local DIST_BASE = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769,
    1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 }
local DIST_EXTRA = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10,
    11, 11, 12, 12, 13, 13 }
local CODE_ORDER = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 }

local function reader(data, start)
    local r = { data = data, pos = start, bit = 0, value = 0 }
    function r:bits(n)
        while self.bit < n do
            local byte = self.data:byte(self.pos)
            if not byte then error("truncated data") end
            self.pos = self.pos + 1
            self.value = self.value | (byte << self.bit)
            self.bit = self.bit + 8
        end
        local result = self.value & ((1 << n) - 1)
        self.value = self.value >> n
        self.bit = self.bit - n
        return result
    end
    function r:align()
        self.value, self.bit = 0, 0
    end
    return r
end

-- Canonical Huffman table: counts per length and symbols in code order.
local function huffman(lengths, n)
    local counts, offsets, symbols = {}, {}, {}
    for len = 0, 15 do counts[len] = 0 end
    for i = 0, n - 1 do counts[lengths[i] or 0] = counts[lengths[i] or 0] + 1 end
    counts[0] = 0
    offsets[1] = 0
    for len = 1, 14 do offsets[len + 1] = offsets[len] + counts[len] end
    for i = 0, n - 1 do
        local len = lengths[i] or 0
        if len ~= 0 then
            symbols[offsets[len]] = i
            offsets[len] = offsets[len] + 1
        end
    end
    return { counts = counts, symbols = symbols }
end

local function decode(r, table_)
    local code, first, index = 0, 0, 0
    for len = 1, 15 do
        code = code | r:bits(1)
        local count = table_.counts[len]
        if code - count < first then return table_.symbols[index + (code - first)] end
        index = index + count
        first = (first + count) << 1
        code = code << 1
    end
    error("bad Huffman code")
end

local FIXED_LIT, FIXED_DIST
do
    local lengths = {}
    for i = 0, 143 do lengths[i] = 8 end
    for i = 144, 255 do lengths[i] = 9 end
    for i = 256, 279 do lengths[i] = 7 end
    for i = 280, 287 do lengths[i] = 8 end
    FIXED_LIT = huffman(lengths, 288)
    local dist = {}
    for i = 0, 29 do dist[i] = 5 end
    FIXED_DIST = huffman(dist, 30)
end

local function inflate_block(r, out, lit, dist)
    while true do
        local symbol = decode(r, lit)
        if symbol < 256 then
            out[#out + 1] = symbol
        elseif symbol == 256 then
            return
        else
            symbol = symbol - 257
            local length = LENGTH_BASE[symbol + 1] + r:bits(LENGTH_EXTRA[symbol + 1])
            local d = decode(r, dist)
            local distance = DIST_BASE[d + 1] + r:bits(DIST_EXTRA[d + 1])
            local start = #out - distance
            for i = 1, length do out[#out + 1] = out[start + i] end
        end
    end
end

local function inflate(data, start)
    local r, out = reader(data, start), {}
    repeat
        local final = r:bits(1)
        local kind = r:bits(2)
        if kind == 0 then
            r:align()
            local p = r.pos
            local len = data:byte(p) | (data:byte(p + 1) << 8)
            for i = 0, len - 1 do out[#out + 1] = data:byte(p + 4 + i) end
            r.pos = p + 4 + len
        elseif kind == 1 then
            inflate_block(r, out, FIXED_LIT, FIXED_DIST)
        elseif kind == 2 then
            local nlit, ndist, ncode = r:bits(5) + 257, r:bits(5) + 1, r:bits(4) + 4
            local code_lengths = {}
            for i = 1, ncode do code_lengths[CODE_ORDER[i]] = r:bits(3) end
            local code_table = huffman(code_lengths, 19)
            local lengths, i = {}, 0
            while i < nlit + ndist do
                local symbol = decode(r, code_table)
                if symbol < 16 then
                    lengths[i] = symbol
                    i = i + 1
                else
                    local repeat_, value = 0, 0
                    if symbol == 16 then
                        value = lengths[i - 1]
                        repeat_ = 3 + r:bits(2)
                    elseif symbol == 17 then
                        repeat_ = 3 + r:bits(3)
                    else
                        repeat_ = 11 + r:bits(7)
                    end
                    for _ = 1, repeat_ do
                        lengths[i] = value
                        i = i + 1
                    end
                end
            end
            local lit_lengths, dist_lengths = {}, {}
            for j = 0, nlit - 1 do lit_lengths[j] = lengths[j] end
            for j = 0, ndist - 1 do dist_lengths[j] = lengths[nlit + j] end
            inflate_block(r, out, huffman(lit_lengths, nlit), huffman(dist_lengths, ndist))
        else
            error("bad block type")
        end
    until final == 1
    local chunks = {}
    for i = 1, #out, 4096 do
        chunks[#chunks + 1] = string.char(table.unpack(out, i, math.min(i + 4095, #out)))
    end
    return table.concat(chunks)
end

function SS.gunzip(data)
    local ok, result = pcall(function()
        if data:byte(1) ~= 0x1f or data:byte(2) ~= 0x8b or data:byte(3) ~= 8 then error("not gzip") end
        local flags, pos = data:byte(4), 11
        if flags & 4 ~= 0 then pos = pos + 2 + (data:byte(pos) | (data:byte(pos + 1) << 8)) end
        if flags & 8 ~= 0 then pos = data:find("\0", pos, true) + 1 end
        if flags & 16 ~= 0 then pos = data:find("\0", pos, true) + 1 end
        if flags & 2 ~= 0 then pos = pos + 2 end
        return inflate(data, pos)
    end)
    return ok and result or nil
end

-- JSON -----------------------------------------------------------------------------------
-- null becomes SS.NULL so override removals survive.

local function json_error(pos) error("bad JSON at " .. pos) end

local ESCAPES = { ['"'] = '"', ["\\"] = "\\", ["/"] = "/", b = "\b", f = "\f", n = "\n", r = "\r", t = "\t" }

local function skip(text, pos)
    return text:find("[^ \t\r\n]", pos) or #text + 1
end

local parse_value

local function parse_string(text, pos)
    local out, i = {}, pos + 1
    while true do
        local c = text:sub(i, i)
        if c == '"' then return table.concat(out), i + 1 end
        if c == "" then json_error(i) end
        if c == "\\" then
            local e = text:sub(i + 1, i + 1)
            if e == "u" then
                local code = tonumber(text:sub(i + 2, i + 5), 16)
                if code >= 0xD800 and code <= 0xDBFF and text:sub(i + 6, i + 7) == "\\u" then
                    local low = tonumber(text:sub(i + 8, i + 11), 16)
                    code = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00)
                    i = i + 6
                end
                out[#out + 1] = utf8.char(code)
                i = i + 6
            else
                out[#out + 1] = ESCAPES[e] or json_error(i)
                i = i + 2
            end
        else
            local j = text:find('["\\]', i)
            if not j then json_error(i) end
            out[#out + 1] = text:sub(i, j - 1)
            i = j
        end
    end
end

parse_value = function(text, pos)
    pos = skip(text, pos)
    local c = text:sub(pos, pos)
    if c == "{" then
        local result = {}
        pos = skip(text, pos + 1)
        if text:sub(pos, pos) == "}" then return result, pos + 1 end
        while true do
            local key
            key, pos = parse_string(text, skip(text, pos))
            pos = skip(text, pos)
            if text:sub(pos, pos) ~= ":" then json_error(pos) end
            result[key], pos = parse_value(text, pos + 1)
            pos = skip(text, pos)
            local sep = text:sub(pos, pos)
            if sep == "}" then return result, pos + 1 end
            if sep ~= "," then json_error(pos) end
            pos = pos + 1
        end
    elseif c == "[" then
        local result = {}
        pos = skip(text, pos + 1)
        if text:sub(pos, pos) == "]" then return result, pos + 1 end
        while true do
            result[#result + 1], pos = parse_value(text, pos)
            pos = skip(text, pos)
            local sep = text:sub(pos, pos)
            if sep == "]" then return result, pos + 1 end
            if sep ~= "," then json_error(pos) end
            pos = pos + 1
        end
    elseif c == '"' then
        return parse_string(text, pos)
    elseif text:sub(pos, pos + 3) == "true" then
        return true, pos + 4
    elseif text:sub(pos, pos + 4) == "false" then
        return false, pos + 5
    elseif text:sub(pos, pos + 3) == "null" then
        return SS.NULL, pos + 4
    else
        local number = text:match("^-?%d+%.?%d*[eE]?[-+]?%d*", pos)
        if not number or number == "" then json_error(pos) end
        return math.tointeger(tonumber(number)) or tonumber(number), pos + #number
    end
end

function SS.json(text)
    local ok, value = pcall(parse_value, text, 1)
    return ok and value or nil
end
