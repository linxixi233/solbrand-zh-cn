extends SceneTree
const MISSION := "The Captain told me to follow my predecessor's air line to ensure it's in working order."
const MISSION_ZH := "船长让我沿着前任留下的供气管线检查，确认它是否运作正常。"
func _initialize() -> void:
	_run.call_deferred()
func _run() -> void:
	assert(ProjectSettings.load_resource_pack(OS.get_cmdline_user_args()[0]))
	var patch = load("res://ZhPatch/ZhPatch.gd").new()
	root.add_child(patch)
	assert(patch.process_mode == Node.PROCESS_MODE_ALWAYS)
	assert(patch._translate_display("C O N T I N U E") == "继续")
	assert(patch._translate_display("♦ " + MISSION + "\n◆ Continue") == "♦ " + MISSION_ZH + "\n◆ 继续")
	var logg := Control.new()
	logg.name = "LoggPanel"
	root.add_child(logg)
	var log := RichTextLabel.new()
	log.name = "MissionLog"
	log.bbcode_enabled = true
	log.append_text("♦ " + MISSION)
	logg.add_child(log)
	await process_frame
	patch._process(0.31)
	assert(log.text.is_empty(), "The append-only API contract must remain unchanged")
	assert(log.get_parsed_text() == "♦ " + MISSION_ZH)
	log.append_text("\n◆ " + MISSION)
	patch._process(0.31)
	assert(log.get_parsed_text() == "♦ " + MISSION_ZH + "\n◆ " + MISSION_ZH)
	# Refreshing the game log clears and appends English again.
	log.clear()
	log.append_text(MISSION)
	patch._process(0.31)
	assert(log.get_parsed_text() == MISSION_ZH)
	var linked_log := RichTextLabel.new()
	linked_log.bbcode_enabled = true
	linked_log.append_text("[url=res://original]Denner Destrøyer[/url]")
	logg.add_child(linked_log)
	await process_frame
	patch._process(0.31)
	assert(linked_log.get_parsed_text() == "Denner Destrøyer", "Do not rebuild unrelated clickable log entries")
	var menu := RichTextLabel.new()
	menu.name = "ContinueLabel"
	menu.append_text("C O N T I N U E")
	root.add_child(menu)
	paused = true
	await process_frame
	await process_frame
	patch._process(0.31)
	assert(menu.get_parsed_text() == "继续", "Paused menus must still be translated")
	var regular := Label.new()
	regular.text = "Continue"
	root.add_child(regular)
	await process_frame
	patch._process(0.31)
	assert(regular.tr(regular.text) == "继续")
	assert(regular.get_theme_font("font").has_char(0x7ee7))
	regular.text = "C O N T I N U E"
	patch._process(0.31)
	assert(regular.tr(regular.text) == "继续", "Letter-spaced plain Label must be detected too")
	var manual := "Restore and study PERIODICALS burnt b[x]y the sun to gle[/x]an critical information in [x]your search for answers."
	assert(patch._translate_display(manual) == "修复并研究被太阳灼损的期刊，获取关键信息，寻找答案。")
	assert(not "[x]" in patch._translate_display(manual))
	var unrelated := RichTextLabel.new()
	unrelated.bbcode_enabled = true
	unrelated.append_text("[url=res://original]Unknown unchanged text[/url]")
	root.add_child(unrelated)
	await process_frame
	patch._process(0.31)
	assert(unrelated.get_parsed_text() == "Unknown unchanged text")
	paused = false
	for node in [logg,menu,regular,unrelated]: node.queue_free()
	await process_frame
	patch._process(0.31)
	assert(patch.controls.is_empty())
	print("PASS: appended mission logs, repeated refresh, letter-spaced CONTINUE, paused menus, complete periodical instructions")
	quit()
