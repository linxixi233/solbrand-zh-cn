extends Node

var dictionary: Dictionary = {}
var cjk_font: FontFile
var checked_fonts: Dictionary = {}
var controls: Dictionary = {}
var pending_controls: Dictionary = {}
var elapsed: float = 0.0
var diagnostic: bool = false
var seen: Dictionary = {}
var runtime_translation: Translation
var display_names: Array[Dictionary] = []
var markup := RegEx.create_from_string("\\[[^\\]]*\\]")
var captions: Dictionary = {}
var button_labels: Dictionary = {}
var latin_words := RegEx.create_from_string("[A-Za-z]{2,}")
var spaced_letters := RegEx.create_from_string("^[A-Za-z](?:[ \\t]+[A-Za-z]){2,}$")
const APPENDED_MISSION := "The Captain told me to follow my predecessor's air line to ensure it's in working order."

func _enter_tree() -> void:
	# Pause menus and their late-created labels must still be localized.
	process_mode = Node.PROCESS_MODE_ALWAYS
	var data = JSON.parse_string(FileAccess.get_file_as_string("res://ZhPatch/zh_CN.json"))
	if data is Dictionary:
		dictionary = data
	var translation := Translation.new()
	runtime_translation = translation
	translation.locale = "zh_CN"
	for original in dictionary:
		translation.add_message(original, dictionary[original])
	TranslationServer.add_translation(translation)
	TranslationServer.set_locale("zh_CN")
	var names_data = JSON.parse_string(FileAccess.get_file_as_string("res://ZhPatch/names.json"))
	if names_data is Dictionary:
		var keys: Array = names_data.keys()
		keys.sort_custom(func(a, b): return a.length() > b.length())
		for original in keys:
			var escaped: String = original
			for character in ["\\", ".", "+", "*", "?", "(", ")", "[", "]", "{", "}", "^", "$", "|"]:
				escaped = escaped.replace(character, "\\" + character)
			display_names.append({"pattern": RegEx.create_from_string("(?i)(?<![A-Za-z])" + escaped + "(?![A-Za-z])"), "translation": names_data[original]})
	if FileAccess.file_exists("res://ZhPatch/captions.json"):
		var caption_data = JSON.parse_string(FileAccess.get_file_as_string("res://ZhPatch/captions.json"))
		if caption_data is Dictionary:
			captions = caption_data
	if FileAccess.file_exists("res://ZhPatch/buttons.json"):
		var button_data = JSON.parse_string(FileAccess.get_file_as_string("res://ZhPatch/buttons.json"))
		if button_data is Dictionary:
			button_labels = button_data
	for font_path in ["C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simsun.ttc"]:
		if FileAccess.file_exists(font_path):
			cjk_font = FontFile.new()
			cjk_font.data = FileAccess.get_file_as_bytes(font_path)
			break
	diagnostic = FileAccess.file_exists(OS.get_executable_path().get_base_dir().path_join("diagnostic.flag"))
	if not get_tree().node_added.is_connected(_node_added):
		get_tree().node_added.connect(_node_added)
	print("[ZH] Loaded ", dictionary.size(), " translations; CJK font: ", cjk_font != null)

func _node_added(node: Node) -> void:
	if node is BaseButton:
		_localize_button.call_deferred(weakref(node))
	if node is Sprite2D:
		_attach_caption.call_deferred(weakref(node))
	if node is Control or node is Label3D:
		var id := node.get_instance_id()
		var reference: WeakRef = controls.get(id)
		if reference == null or reference.get_ref() != node:
			reference = weakref(node)
			controls[id] = reference
		pending_controls[id] = true
		# Reparenting can emit node_added repeatedly without another ready signal.
		# Defer initialization instead of connecting another ready callback.
		_prepare.call_deferred(reference)

func _prepare(reference: WeakRef) -> void:
	var control = reference.get_ref()
	if control == null or not control.is_inside_tree() or not control.is_node_ready():
		return
	pending_controls.erase(control.get_instance_id())
	if control is Label3D:
		control.auto_translate_mode = Node.AUTO_TRANSLATE_MODE_ALWAYS
		_add_fallback(control.font)
		return
	if control is Label or control is RichTextLabel or control is BaseButton:
		control.auto_translate_mode = Node.AUTO_TRANSLATE_MODE_ALWAYS
	if control is Label and control.label_settings != null:
		_add_fallback(control.label_settings.font)
	for font_name in ["font", "normal_font", "bold_font", "italics_font", "bold_italics_font", "mono_font"]:
		if control.has_theme_font(font_name):
			_add_fallback(control.get_theme_font(font_name))

func _add_fallback(font: Font) -> void:
	if font == null or cjk_font == null or font == cjk_font:
		return
	var id := font.get_instance_id()
	if checked_fonts.has(id) and checked_fonts[id].get_ref() == font and font.fallbacks.has(cjk_font):
		return
	checked_fonts[id] = weakref(font)
	if font is FontVariation:
		_add_fallback(font.base_font)
	var existing := font.fallbacks
	if not existing.has(cjk_font):
		existing.append(cjk_font)
		font.fallbacks = existing

func _process(delta: float) -> void:
	elapsed += delta
	if elapsed < 0.3:
		return
	elapsed = 0.0
	for id in controls.keys():
		var reference: WeakRef = controls[id]
		var control = reference.get_ref()
		if control == null:
			controls.erase(id)
			pending_controls.erase(id)
			continue
		if not control.is_visible_in_tree():
			continue
		# Game code can replace LabelSettings, themes and FontVariations after ready.
		_prepare(reference)
		if control is RichTextLabel:
			_refresh_appended_text(control)
		if control is Label or control is RichTextLabel or control is Button or control is Label3D:
			var original: String = control.text
			if not original.is_empty() and not dictionary.has(original) and (latin_words.search(original) != null or spaced_letters.search(original.strip_edges()) != null) and not seen.has("translation:" + original):
				seen["translation:" + original] = true
				var translated := _translate_display(original)
				if translated != original:
					runtime_translation.add_message(original, translated)
					control.notification(NOTIFICATION_TRANSLATION_CHANGED)
		if control is Label or control is RichTextLabel or control is Button:
			# Native automatic translation preserves the original text used by game code.
			if diagnostic and not control.text.is_empty() and not seen.has(control.text):
				seen[control.text] = true
				var log_file := FileAccess.open(OS.get_executable_path().get_base_dir().path_join("visible-texts.jsonl"), FileAccess.READ_WRITE)
				if log_file == null:
					log_file = FileAccess.open(OS.get_executable_path().get_base_dir().path_join("visible-texts.jsonl"), FileAccess.WRITE)
				if log_file != null:
					log_file.seek_end()
					log_file.store_line(JSON.stringify({"path": str(control.get_path()), "text": control.text}))

func _translate_piece(text: String) -> String:
	if text.contains("\n"):
		var lines := PackedStringArray()
		for line in text.split("\n"):
			lines.append(_translate_piece(line))
		return "\n".join(lines)
	var stripped := text.strip_edges()
	if stripped.is_empty():
		return text
	if dictionary.has(stripped):
		return text.replace(stripped, dictionary[stripped])
	# Some menu labels insert spaces between the English letters.
	if spaced_letters.search(stripped) != null:
		var compact := stripped.replace(" ", "").replace("\t", "")
		if dictionary.has(compact):
			return text.replace(stripped, dictionary[compact])
	var bullet := RegEx.create_from_string("^([ \\t•◆◇♦◈▪▸▶*—–-]+)(.*)$").search(text)
	if bullet != null and dictionary.has(bullet.get_string(2).strip_edges()):
		return bullet.get_string(1) + _translate_piece(bullet.get_string(2))
	var result := text
	for entry in display_names:
		result = entry.pattern.sub(result, entry.translation, true)
	var items := RegEx.create_from_string("(?i)^(\\d+) items?$").search(result.strip_edges())
	if items != null:
		return items.get_string(1) + " 项"
	return result

func _translate_display(text: String) -> String:
	if dictionary.has(text):
		return dictionary[text]
	var result := ""
	var cursor := 0
	for matched in markup.search_all(text):
		result += _translate_piece(text.substr(cursor, matched.get_start() - cursor))
		result += matched.get_string()
		cursor = matched.get_end()
	return result + _translate_piece(text.substr(cursor))

func _refresh_appended_text(control: RichTextLabel) -> void:
	# append_text()/push_*() do not populate RichTextLabel.text, so native
	# automatic translation never sees these generated log/menu entries.
	if not control.text.is_empty():
		return
	var source := control.get_parsed_text()
	if source.is_empty():
		return
	var stripped := source.strip_edges()
	# Other log screens contain clickable links and deliberate rich formatting.
	# Rebuild only the compiled-in mission line that bypasses resource translation.
	var log_entry := "logg" in str(control.get_path()).to_lower() and source.contains(APPENDED_MISSION)
	var menu_key := stripped.replace(" ", "").replace("\t", "").to_upper()
	var menu_entry := not "logg" in str(control.get_path()).to_lower() and menu_key in ["CONTINUE", "CONTINUEGAME", "RESUME", "RESUMEGAME", "NEWGAME", "OPTIONS", "SETTINGS", "BACK", "RETURN", "QUIT", "EXIT", "QUITGAME", "MAINMENU", "LOADGAME"]
	if not log_entry and not menu_entry:
		return
	var translated := _translate_display(source)
	if translated == source:
		return
	# Keep .text empty and leave the mission/save data untouched. The game
	# remains free to clear or append more entries on its next refresh.
	control.clear()
	control.append_text(translated)

func _attach_caption(reference: WeakRef) -> void:
	var sprite = reference.get_ref()
	if sprite == null or sprite.texture == null or sprite.has_meta("solbrand_zh_caption"):
		return
	var texture_path: String = sprite.texture.resource_path
	for key in captions:
		if not key in texture_path:
			continue
		sprite.set_meta("solbrand_zh_caption", true)
		var dimensions: Vector2 = sprite.texture.get_size()
		var panel := PanelContainer.new()
		panel.name = "ChineseReading"
		panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
		panel.position = Vector2(-dimensions.x * 0.5 if sprite.centered else 0.0, dimensions.y * 0.5 + 32.0 if sprite.centered else dimensions.y + 32.0)
		panel.custom_minimum_size.x = dimensions.x
		var style := StyleBoxFlat.new()
		style.bg_color = Color(0.93, 0.95, 0.9, 0.96)
		style.content_margin_left = 32
		style.content_margin_right = 32
		style.content_margin_top = 24
		style.content_margin_bottom = 24
		panel.add_theme_stylebox_override("panel", style)
		var label := RichTextLabel.new()
		label.name = "ChineseText"
		label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		label.bbcode_enabled = true
		label.fit_content = true
		label.scroll_active = false
		label.custom_minimum_size.x = maxf(256.0, dimensions.x - 64.0)
		label.text = "[b]中文译文[/b]\n" + str(captions[key])
		for font_name in ["normal_font", "bold_font", "italics_font", "bold_italics_font", "mono_font"]:
			label.add_theme_font_override(font_name, cjk_font)
		for size_name in ["normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size"]:
			label.add_theme_font_size_override(size_name, clampi(int(dimensions.x / 32), 24, 72))
		label.add_theme_color_override("default_color", Color(0.08, 0.12, 0.18))
		panel.add_child(label)
		sprite.add_child(panel)

func _localize_button(reference: WeakRef) -> void:
	var button = reference.get_ref()
	if button == null or button.has_meta("solbrand_zh_button") or not button_labels.has(str(button.name)):
		return
	button.set_meta("solbrand_zh_button", true)
	var translated: String = button_labels[str(button.name)]
	if button.tooltip_text.is_empty():
		button.tooltip_text = translated
	if button is Button and not button.text.is_empty():
		return
	for child in button.find_children("*", "Label", true, false):
		if child.text.length() > 1 and (dictionary.has(child.text) or child.text.contains(translated)):
			return
	if button.size.x < 64.0 or button.size.y < 24.0:
		return
	var label := Label.new()
	label.name = "ChineseButtonText"
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.text = translated
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_font_override("font", cjk_font)
	label.add_theme_font_size_override("font_size", clampi(int(button.size.x / maxf(3.0, translated.length() + 1.0)), 12, 24))
	label.add_theme_color_override("font_color", Color.WHITE)
	var background := StyleBoxFlat.new()
	background.bg_color = Color(0.03, 0.05, 0.08, 0.9)
	label.add_theme_stylebox_override("normal", background)
	button.add_child(label)
	label.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
