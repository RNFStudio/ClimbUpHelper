local controller = {}

controller.name = "ChroniaHelper/RippleWaterfallNoDashRefillController"
controller.depth = -1000010

controller.placements = {
    {
        name = "controller",
        data = {
            inset = 0
        }
    }
}

controller.fieldOrder = {
    "x", "y",
    "inset"
}

controller.ignoredFields = { "width", "height" }

controller.sprite = function(room, entity)
    return {
        require("structs.drawable_sprite").fromTexture("ChroniaHelper/LoennIcons/Waterfall", entity)
    }
end

return controller
