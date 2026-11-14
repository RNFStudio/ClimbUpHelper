local bloom = {}

bloom.name = "ChroniaHelper/Bloom"
bloom.canBackground = true
bloom.canForeground = false

bloom.defaultData = {
    strength = 0.5,
    blur = 1.5,
}

bloom.fieldOrder = {
    "only", "exclude", "tag", "flag", "notflag",
    "strength", "blur",
}

bloom.fieldInformation = {
    strength = {
        fieldType = "number",
        minimumValue = 0.0,
        maximumValue = 2.0,
    },
    blur = {
        fieldType = "number",
        minimumValue = 0.1,
        maximumValue = 4.0,
    },
}

return bloom
