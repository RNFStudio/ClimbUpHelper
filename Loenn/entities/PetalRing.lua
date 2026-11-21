local utils = require("utils")
local drawableSprite = require("structs.drawable_sprite")

local petalRing = {}

petalRing.name = "ChroniaHelper/PetalRing"

petalRing.placements = {
    name = "petal_ring",
    data = {
        radius = 8.0,
        petal_size = 6.4,
        petal_count = 8,
        rotation_speed = 30.0,
        bloom_alpha = 1.0,
        alpha = 1.0,
        depth = -50000,
    },
}

petalRing.fieldOrder = {
    "x", "y",
    "radius", "petal_size", "petal_count",
    "rotation_speed",
    "bloom_alpha", "alpha",
    "depth",
}

petalRing.fieldInformation = {
    radius = {
        fieldType = "number",
        minimumValue = 0.0,
    },
    petal_size = {
        fieldType = "number",
        minimumValue = 0.0,
    },
    petal_count = {
        fieldType = "integer",
        minimumValue = 1,
    },
    rotation_speed = {
        fieldType = "number",
    },
    bloom_alpha = {
        fieldType = "number",
        minimumValue = 0.0,
        maximumValue = 1.0,
    },
    alpha = {
        fieldType = "number",
        minimumValue = 0.0,
        maximumValue = 1.0,
    },
    depth = require("mods").requireFromPlugin("helpers.field_options", "ChroniaHelper").depths,
}

petalRing.depth = function(room, entity)
    return entity.depth or -50000
end

-- 花瓣长度由 petal_size 直接控制
local function getPetalSize(entity)
    local radius = entity.radius or 8
    local petalSize = entity.petal_size or 6.4

    return radius, petalSize
end

-- 选择/碰撞体积：覆盖整圈花瓣
petalRing.rectangle = function(room, entity)
    local radius, petalSize = getPetalSize(entity)
    local extent = radius + petalSize * 1.5

    return utils.rectangle(entity.x - extent, entity.y - extent, extent * 2, extent * 2)
end

petalRing.selection = function(room, entity)
    return petalRing.rectangle(room, entity)
end

-- 与游戏内一致的“按坐标生成初始角度”
local function initialRotation(x, y)
    local value = math.sin(x * 12.9898 + y * 78.233) * 43758.5453
    return (value - math.floor(value)) * math.pi * 2
end

-- 编辑器里画出花瓣
petalRing.sprite = function(room, entity)
    local sprites = {}
    local x, y = entity.x or 0, entity.y or 0
    local radius, petalSize = getPetalSize(entity)
    local count = entity.petal_count or 8
    local length = petalSize * 1.5
    local width = petalSize
    local alpha = entity.alpha or 1
    local baseRotation = initialRotation(x, y)

    for i = 0, count - 1 do
        local angle = baseRotation + (i / count) * math.pi * 2
        local px = x + math.cos(angle) * radius
        local py = y + math.sin(angle) * radius

        local sprite = drawableSprite.fromTexture("ClimbUpHelper/particles/petal", {
            x = px,
            y = py,
            rotation = angle + math.pi / 2,
            scaleX = width / 16,
            scaleY = length / 16,
            justificationX = 0.5,
            justificationY = 1.0,
        })

        if sprite then
            sprite:setAlpha(alpha)
            table.insert(sprites, sprite)
        end
    end

    return sprites
end

return petalRing
