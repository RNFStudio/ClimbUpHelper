local controller = {}

controller.name = "ChroniaHelper/WaterfallNoGrabController"
controller.depth = -1000010
--controller.fillColor = { 120 / 255, 200 / 255, 255 / 255, 0.25 }
--controller.borderColor = { 120 / 255, 200 / 255, 255 / 255, 0.7 }

controller.placements = {
    {
        name = "controller",
        data = {
            --width = 8,
            --height = 8,
            inset = 2
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
        --require("structs.drawable_rectangle").fromRectangle("bordered", entity.x, entity.y, entity.width, entity.height,{28,69,212,0.2},{255,255,255,1}),
        require("structs.drawable_sprite").fromTexture("ChroniaHelper/LoennIcons/Waterfall", entity)
    }
end

return controller
