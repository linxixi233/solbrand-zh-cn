extends SceneTree

func _initialize() -> void:
	call_deferred("_validate")

func _validate() -> void:
	var arguments := OS.get_cmdline_user_args()
	assert(arguments.size() == 1, "Overlay PCK path required")
	assert(ProjectSettings.load_resource_pack(arguments[0]), "Cannot load overlay")
	var script = load("res://ZhPatch/ZhPatch.gd")
	assert(script != null, "Patch script must compile")
	var patch = script.new()
	root.add_child(patch)
	assert(TranslationServer.get_locale() == "zh_CN")
	assert(TranslationServer.translate("New Game") == "新游戏")
	var label := Label.new()
	label.text = "New Game"
	root.add_child(label)
	await process_frame
	assert(label.get_theme_font("font").has_char(0x65b0), "Font fallback must contain Chinese")
	assert(patch.cjk_font != null)
	assert(patch.cjk_font.has_char(0x620f))
	print("VALIDATED: GDScript, translations, node lifecycle, Chinese glyph coverage")
	quit()
