extends SceneTree

func _initialize() -> void:
	_validate.call_deferred()

func _validate() -> void:
	var args := OS.get_cmdline_user_args()
	assert(args.size() == 1)
	assert(ProjectSettings.load_resource_pack(args[0]))
	var script = load("res://ZhPatch/ZhPatch.gd")
	assert(script != null)
	var patch = script.new()
	root.add_child(patch)
	assert(TranslationServer.translate("New Game") == "新游戏")

	var holder := Control.new()
	var label := Label.new()
	label.text = "New Game"
	holder.add_child(label)
	root.add_child(holder)
	for i in range(20):
		patch._node_added(label)
	await process_frame
	await process_frame
	patch._process(0.31)
	assert(patch.controls.size() == 2, "Each Control must be registered once")
	assert(label.get_theme_font("font").has_char(0x65b0))
	assert(label.auto_translate_mode == Node.AUTO_TRANSLATE_MODE_ALWAYS)
	assert(label.ready.get_connections().is_empty(), "Patch must not connect Control.ready")

	for i in range(100):
		holder.remove_child(label)
		holder.add_child(label)
	await process_frame
	patch._process(0.31)
	assert(patch.controls.size() == 2, "Reparenting must not duplicate registrations")
	assert(label.ready.get_connections().is_empty())

	for i in range(100):
		holder.remove_child(label)
		label.request_ready()
		holder.add_child(label)
	await process_frame
	patch._process(0.31)
	assert(patch.controls.size() == 2)
	assert(label.get_theme_font("font").has_char(0x620f))

	for i in range(1000):
		var transient := Label.new()
		holder.add_child(transient)
		transient.free()
	await process_frame
	patch._process(0.31)
	assert(patch.controls.size() == 2, "Freed Controls must be pruned")
	assert(patch.pending_controls.is_empty())

	holder.remove_child(label)
	await process_frame
	holder.add_child(label)
	await process_frame
	patch._process(0.31)
	assert(patch.controls.size() == 2)
	assert(label.get_theme_font("font").has_char(0x65b0))
	holder.queue_free()
	await process_frame
	await process_frame
	patch._process(0.31)
	assert(patch.controls.is_empty())
	assert(patch.pending_controls.is_empty())
	print("PASS: translations, font fallback, repeated registration, 200 reentries, 1000 freed controls")
	quit(0)
