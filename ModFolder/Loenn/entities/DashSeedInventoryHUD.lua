local hud = {}

hud.name = "ChroniaHelper/DashSeedInventoryHUD"
hud.depth = 0
hud.texture = "@Internal@/northern_lights"

-- fixed 2x2 tiles (16x16 px) marker, never resizable
hud.canResize = { false, false }

hud.placements = {
    name = "hud",
    data = {
        slotWidth = 128.0,
        slotHeight = 72.0,
        spacing = 12.0,
        animationDuration = 0.5,
        slotAlpha = 0.5,
        offsetY = 48.0,
        textScale = 1.0,
        depth = -100,
    }
}

hud.fieldOrder = {
    "x", "y",
    "depth",
    "slotWidth", "slotHeight", "spacing",
    "animationDuration", "slotAlpha",
    "offsetY", "textScale"
}

hud.ignoredFields = { "width", "height" }

hud.fieldInformation = {
    depth = require("mods").requireFromPlugin("helpers.field_options", "ChroniaHelper").depths,
    animationDuration = {
        minimumValue = 0.01,
        maximumValue = 10.0
    },
    slotAlpha = {
        minimumValue = 0.0,
        maximumValue = 1.0
    },
    textScale = {
        minimumValue = 0.1,
        maximumValue = 2.0
    }
}

return hud
