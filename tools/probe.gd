extends SceneTree

func _initialize() -> void:
	print("PROBE_ENGINE ", Engine.get_version_info())
	print("PROBE_MAIN ", ProjectSettings.get_setting("application/run/main_scene", ""))
	print("PROBE_FEATURE_DOTNET ", OS.has_feature("dotnet"))
	print("PROBE_USER ", OS.get_user_data_dir())
	quit()
