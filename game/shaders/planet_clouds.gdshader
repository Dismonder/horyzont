shader_type spatial;
// Cloud layer of a planet (PlanetClouds): a sphere inside the atmosphere with drifting noise coverage, lit by the sun on both
// sides, so it reads as clouds from the ground and from orbit. Visual only.
render_mode blend_mix, cull_disabled, depth_draw_never, shadows_disabled, specular_disabled;

uniform float u_coverage : hint_range(0.0, 1.0) = 0.46;
uniform float u_softness = 0.16;
uniform float u_scale = 0.0019;
uniform float u_speed = 0.004;
uniform vec3 u_color : source_color = vec3(1.0);
uniform vec3 u_shadow : source_color = vec3(0.62, 0.66, 0.74);

varying vec3 v_world;
varying vec3 v_center;

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

float fbm(vec3 x) {
	float sum = 0.0;
	float amplitude = 0.5;
	for (int i = 0; i < 5; i++) {
		sum += value_noise(x) * amplitude;
		x = x * 2.07 + vec3(11.3, 5.1, 7.7);
		amplitude *= 0.5;
	}
	return sum;
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_center = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
}

void fragment() {
	vec3 rel = v_world - v_center;
	vec3 drift = vec3(TIME * u_speed * 40.0, 0.0, TIME * u_speed * 25.0);
	float n = fbm(rel * u_scale + drift);
	float density = smoothstep(1.0 - u_coverage, 1.0 - u_coverage + u_softness, n);
	// Thicker parts are darker underneath.
	float thick = smoothstep(1.0 - u_coverage, 1.0, n);
	ALBEDO = mix(u_color, u_shadow, thick * 0.6);
	// Seen edge-on (the limb of the planet from orbit, the horizon from the ground) the layer would pile up: fade it there.
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	ALPHA = density * 0.9 * smoothstep(0.08, 0.45, facing);
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
}

void light() {
	// Clouds scatter light all around: soft wrap lighting instead of a hard terminator.
	float wrap = clamp((dot(NORMAL, LIGHT) + 0.6) / 1.6, 0.0, 1.0);
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * wrap / PI;
}
