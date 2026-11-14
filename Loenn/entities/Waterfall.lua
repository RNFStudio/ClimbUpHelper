local drawableRectangle = require("structs.drawable_rectangle")
local utils = require("utils")

local waterfall = {}

waterfall.name = "ChroniaHelper/Waterfall"
waterfall.canResize = { true, true }
waterfall.minimumSize = { 8, 8 }

waterfall.placements = {
    name = "waterfall",
    data = {
        width = 16,
        height = 64,
        fill_color = "87cefa4d",
        outline_color = "87cefacc",
        ripple_strength = 0.75,
        depth = -9999,
    },
}

waterfall.fieldOrder = {
    "x", "y",
    "width", "height",
    "fill_color", "outline_color", "ripple_strength", "depth",
}

waterfall.fieldInformation = {
    fill_color = {
        fieldType = "color",
        useAlpha = true,
    },
    outline_color = {
        fieldType = "color",
        useAlpha = true,
    },
    ripple_strength = {
        fieldType = "number",
        minimumValue = 0.0,
    },
    depth = {
        fieldType = "integer",
    },
}

waterfall.depth = function(room, entity)
    return entity.depth or -9999
end

waterfall.sprite = function(room, entity)
    local x, y = entity.x or 0, entity.y or 0
    local width, height = entity.width or 16, entity.height or 64
    local fill = utils.getColor(entity.fill_color or "87cefa4d")
    local outline = utils.getColor(entity.outline_color or "87cefacc")

    return {
        drawableRectangle.fromRectangle("fill", x + 1, y, math.max(width - 2, 0), height, fill),
        drawableRectangle.fromRectangle("fill", x - 1, y, 2, height, outline),
        drawableRectangle.fromRectangle("fill", x + width - 1, y, 2, height, outline),
    }
end

return waterfall
