local utils = require("utils")
local drawableSprite = require("structs.drawable_sprite")

local seed = {}

seed.name = "ChroniaHelper/DashSeed"
seed.placements = {
    name = "seed",
    data = {
        depth = -100,
        persistent = false,
        sprite = "ghostberrySeed",
        spriteColor = "ffffffff",
        --wiggleDuration = 0.5,
        --wiggleFrequency = 4.0,
        --waveFrequency = 0.5,
        --waveOffset = 0,
        bloomAlpha = 1.0,
        bloomRadius = 12.0,
        lightColor = "ffffffff",
        lightStartFade = 16,
        lightEndFade = 24,
        pulseSfx = "event:/game/general/seed_pulse",
        touchSfx = "event:/game/general/seed_touch",
        poofSfx = "event:/game/general/seed_poof",
        appearSfx = "event:/game/general/seed_reappear",
        particleSprite = "particles/shatter",
        particleColor1 = "ffffffff",
        particleColor2 = "808080ff",
        returnTimeMax = 0.4,
        returnTimeMin = 0.2,
        --seedDashes = 1,
    },
}

--[[
seed.fieldOrder = {
    "_x", "_y", "x", "y", "_id", "_name",
    "sprite", "spriteColor", "persistent", "depth",
    "wiggleDuration", "wiggleFrequency", "waveFrequency",
    "bloomAlpha", "bloomRadius", "lightColor", "lightStartFade", "lightEndFade",
    "pulseSfx", "touchSfx", "poofSfx", "appearSfx",
    "particleSprite", "particleColor1", "particleColor2",
}
]]

seed.fieldInformation = {
    depth = require("mods").requireFromPlugin("helpers.field_options", "ChroniaHelper").depths,
    spriteColor = {
        fieldType = "color",
        useAlpha = true,
    },
    lightColor = {
        fieldType = "color",
        useAlpha = true,
    },
    particleColor1 = {
        fieldType = "color",
        useAlpha = true,
    },
    particleColor2 = {
        fieldType = "color",
        useAlpha = true,
    },
    lightStartFade = {
        fieldType = "integer",
    },
    lightEndFade = {
        fieldType = "integer",
    },
    seedDashes = {
        fieldType = "integer",
        minimumValue = 1,
    },
}

seed.sprite = function(room, entity)
    return {
        drawableSprite.fromTexture("collectables/ghostberry/seed00", entity),
    }
end

seed.selection = function(room, entity)
    return utils.rectangle(entity.x - 8, entity.y - 8, 16, 16)
end

return seed
