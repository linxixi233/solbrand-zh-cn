extends SceneTree
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	assert(ProjectSettings.load_resource_pack(args[0]))
	var paths := ["Data/Fragments/UvcRadiogram/uvc_radiogram.png", "Data/Fragments/UvcRadiogram/uvc_excerpt.png", "Data/Fragments/SkandicMapTop/skandic_map.png", "Data/Fragments/ByggeManual/bygge_manual.png"]
	var output := args[1]
	DirAccess.make_dir_recursive_absolute(output)
	for path in paths:
		if ResourceLoader.exists("res://" + path):
			var texture = load("res://" + path)
			if texture is Texture2D:
				texture.get_image().save_png(output.path_join(path.get_file()))
	quit()
