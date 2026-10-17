shader_type spatial;
// Plants scattered by PlanetFlora: the colours of the imported material plus a sway in the wind that grows with the height above
// the ground point (the origin of each instance), with a phase per instance so a meadow does not move in lockstep.
render_mode cull_disabled, depth_draw_opaque;

uniform vec3 u_albedo : source_color = vec3(0.4, 0.6, 0.3);
uniform float u_roughness = 0.85;
uniform vec3 u_emission : source_color = vec3(0.0);
uniform float u_emission_energy = 0.0;
// Sway amplitude (metres) at u_height above the ground.
uniform float u_sway = 0.08;
uniform float u_height = 1.0;

void vertex() {
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float phase = dot(origin, vec3(0.173, 0.071, 0.113));
	float bend = max(VERTEX.y, 0.0) / u_height;
	bend *= bend;
	float gust = 0.6 + 0.4 * sin(TIME * 0.45 + phase * 0.21);
	VERTEX.x += sin(TIME * 1.9 + phase) * u_sway * bend * gust;
	VERTEX.z += cos(TIME * 1.4 + phase * 1.31) * u_sway * 0.6 * bend * gust;
}

void fragment() {
	ALBEDO = u_albedo;
	ROUGHNESS = u_roughness;
	EMISSION = u_emission * u_emission_energy;
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
}
