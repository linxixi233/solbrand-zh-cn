extends SceneTree
func _initialize() -> void:
	_validate.call_deferred()

func _validate() -> void:
	var args := OS.get_cmdline_user_args()
	assert(ProjectSettings.load_resource_pack(args[0]))
	var patch = load("res://ZhPatch/ZhPatch.gd").new()
	root.add_child(patch)
	assert(patch._translate_display("[b]Denner Destrøyer[/b]") == "[b]丹纳驱逐舰[/b]")
	assert(patch._translate_display("[url=res://no_change]Riddartorget[/url]") == "[url=res://no_change]骑士广场[/url]")
	assert(patch._translate_display("3 items") == "3 项")
	var label := Label.new()
	label.text = "New Game"
	root.add_child(label)
	await process_frame
	var settings := LabelSettings.new()
	var late_font := SystemFont.new()
	late_font.font_names = PackedStringArray(["Arial"])
	settings.font = late_font
	label.label_settings = settings
	patch._process(0.31)
	assert(late_font.fallbacks.has(patch.cjk_font), "Font assigned after ready must be repaired")
	var rich := RichTextLabel.new()
	rich.bbcode_enabled = true
	rich.text = "[b]Denner Destrøyer[/b]"
	root.add_child(rich)
	await process_frame
	var variation := FontVariation.new()
	var base := SystemFont.new()
	base.font_names = PackedStringArray(["Arial"])
	variation.base_font = base
	rich.add_theme_font_override("bold_font", variation)
	patch._process(0.31)
	assert(variation.fallbacks.has(patch.cjk_font))
	assert(base.fallbacks.has(patch.cjk_font))
	assert(rich.tr(rich.text) == "[b]丹纳驱逐舰[/b]")
	var label3d := Label3D.new()
	label3d.text = "Grand Square"
	label3d.font = SystemFont.new()
	root.add_child(label3d)
	await process_frame
	patch._process(0.31)
	assert(label3d.font.fallbacks.has(patch.cjk_font))
	assert(label3d.tr(label3d.text) == "大广场")
	var button := TextureButton.new()
	button.name = "AcceptButton"
	button.size = Vector2(144, 64)
	root.add_child(button)
	await process_frame
	patch._process(0.31)
	assert(button.tooltip_text == "进入")
	assert(button.has_node("ChineseButtonText"))
	var sprite := Sprite2D.new()
	var texture := ImageTexture.create_from_image(Image.create(512, 256, false, Image.FORMAT_RGBA8))
	texture.take_over_path("res://Data/Fragments/UvcRadiogram/uvc_radiogram.png")
	sprite.texture = texture
	root.add_child(sprite)
	await process_frame
	await process_frame
	patch._process(0.31)
	assert(sprite.has_node("ChineseReading/ChineseText"))
	assert(sprite.get_node("ChineseReading/ChineseText").text.contains("曼弗雷德"))
	for node in [label, rich, label3d, button, sprite]:
		node.queue_free()
	await process_frame
	await process_frame
	patch._process(0.31)
	assert(patch.controls.is_empty())
	print("PASS: late fonts, FontVariation, BBCode names, Label3D, TextureButton, Arkiv image reading")
	quit()
