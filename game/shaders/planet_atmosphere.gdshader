shader_type spatial;
// Glow of the atmosphere around a planet seen from above it (PlanetAtmosphere): a rim that only the sun side lights. Inside the
// shell the sky shader does this job, so the shell discards itself there.
render_mode blend_add, cull_back, depth_draw_never, depth_test_disabled, ambient_light_disabled, specular_disabled, shadows_disabled;

uniform vec3 u_color : source_color = vec3(0.35, 0.6, 1.0);
uniform float u_radius = 4200.0;
uniform float u_strength = 1.6;

varying vec3 v_world;
varying vec3 v_center;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_center = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
}

void fragment() {
	if (length(CAMERA_POSITION_WORLD - v_center) < u_radius) {
		discard;
	}

	ALBEDO = u_color;
}

void light() {
	float rim = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 4.0);
	float lit = smoothstep(-0.3, 0.4, dot(normalize(NORMAL), LIGHT));
	DIFFUSE_LIGHT += vec3(rim * lit * u_strength);
}
