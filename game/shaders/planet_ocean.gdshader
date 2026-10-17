shader_type spatial;
// Visual sea of a planet (PlanetOcean): a sphere at the sea level with moving small waves, colour by the depth of water behind it
// and foam along the shore. Purely visual: the character walks on the sea floor.
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;

uniform sampler2D u_depth : hint_depth_texture, filter_nearest;
uniform vec3 u_shallow : source_color = vec3(0.13, 0.55, 0.58);
uniform vec3 u_deep : source_color = vec3(0.03, 0.17, 0.32);
uniform float u_clarity = 7.0;
uniform float u_wave_scale = 0.18;
uniform float u_wave_speed = 0.35;

varying vec3 v_world;
varying vec3 v_up;

float hash31(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.71, 0.113, 0.419));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float value_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(hash31(i + vec3(0, 0, 0)), hash31(i + vec3(1, 0, 0)), f.x),
			mix(hash31(i + vec3(0, 1, 0)), hash31(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash31(i + vec3(0, 0, 1)), hash31(i + vec3(1, 0, 1)), f.x),
			mix(hash31(i + vec3(0, 1, 1)), hash31(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float waves(vec3 p) {
	float t = TIME * u_wave_speed;
	return value_noise(p * u_wave_scale + vec3(t, 0.0, t * 0.7))
		+ 0.5 * value_noise(p * u_wave_scale * 2.7 - vec3(t * 1.3, t * 0.4, 0.0))
		+ 0.25 * value_noise(p * u_wave_scale * 7.1 + vec3(0.0, t * 2.1, t));
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_up = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	// Wave normal from the gradient of the wave height in the tangent plane.
	vec3 up = normalize(v_up);
	vec3 tangent = normalize(cross(up, abs(up.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
	vec3 bitangent = cross(up, tangent);
	float e = 0.6;
	float h0 = waves(v_world);
	float hx = waves(v_world + tangent * e);
	float hz = waves(v_world + bitangent * e);
	vec3 world_normal = normalize(up - (tangent * (hx - h0) + bitangent * (hz - h0)) * 0.55);
	NORMAL = normalize((VIEW_MATRIX * vec4(world_normal, 0.0)).xyz);
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}

	// Thickness of the water between this surface and whatever lies behind it.
	float depth = texture(u_depth, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	behind.xyz /= behind.w;
	float thickness = max(VERTEX.z - behind.z, 0.0);
	float deep = 1.0 - exp(-thickness / u_clarity);

	float foam = (1.0 - smoothstep(0.0, 0.9, thickness)) * smoothstep(0.35, 0.75, value_noise(v_world * 1.7 + vec3(TIME * 0.3)));
	vec3 color = mix(u_shallow, u_deep, deep);
	ALBEDO = mix(color, vec3(0.9, 0.95, 0.97), foam);
	ALPHA = clamp(mix(0.35, 0.96, deep) + foam, 0.0, 1.0);
	ROUGHNESS = mix(0.04, 0.6, foam);
	SPECULAR = 0.6;
	METALLIC = 0.0;
}
