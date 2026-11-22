local gaussianBlur = {}

gaussianBlur.name = "ChroniaHelper/GaussianBlur"
gaussianBlur.canBackground = true
gaussianBlur.canForeground = true

gaussianBlur.defaultData = {
    mode = "additive",
    strength = 1.0,
    alpha = 1.0,
    blur = 1.0,
}

gaussianBlur.fieldOrder = {
    "only", "exclude", "tag", "flag", "notflag",
    "mode", "strength", "alpha", "blur",
}

gaussianBlur.fieldInformation = {
    mode = {
        options = {
            { "Additive", "additive" },
            { "Alpha Blend", "alpha" },
        },
        editable = false,
    },
    strength = {
        fieldType = "number",
        minimumValue = 0.0,
    },
    alpha = {
        fieldType = "number",
        minimumValue = 0.0,
        maximumValue = 1.0,
    },
    blur = {
        fieldType = "number",
        minimumValue = 0.1,
        maximumValue = 4.0,
    },
}

return gaussianBlur
