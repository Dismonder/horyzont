shader_type spatial;
render_mode cull_back;

// One layer per block material; UV2.x holds the layer. Front faces are clockwise (Godot default).
uniform sampler2DArray blocks : source_color, filter_nearest, repeat_enable;

void fragment() {
	vec4 texel = texture(blocks, vec3(UV, UV2.x));
	ALBEDO = texel.rgb;
	ROUGHNESS = 0.9;
}
