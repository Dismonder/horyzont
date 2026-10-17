shader_type sky;
// Sky of a planet seen from its surface or from space. Everything is in world space: u_up is the local "up" at the camera and
// u_sun the direction towards the sun, both set every frame by PlanetSky; the sky itself is never rotated. The stars turn with the
// sun (u_stars), so the night sky moves over the day like a real one.

uniform vec3 u_up = vec3(0.0, 1.0, 0.0);
uniform vec3 u_sun = vec3(0.0, 1.0, 0.0);
uniform mat3 u_stars = mat3(1.0);
// 1 inside the atmosphere, 0 in space.
uniform float u_atmosphere : hint_range(0.0, 1.0) = 1.0;

uniform vec3 u_day_zenith : source_color = vec3(0.17, 0.39, 0.80);
uniform vec3 u_day_horizon : source_color = vec3(0.66, 0.79, 0.93);
uniform vec3 u_sunset : source_color = vec3(1.0, 0.47, 0.20);
uniform vec3 u_night_zenith : source_color = vec3(0.006, 0.009, 0.025);
uniform vec3 u_night_horizon : source_color = vec3(0.03, 0.04, 0.08);
uniform vec3 u_ground : source_color = vec3(0.28, 0.30, 0.30);

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

vec3 hash33(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yxz + 33.33);
	return fract((p.xxy + p.yxx) * p.zyx);
}

float stars(vec3 dir) {
	vec3 d = u_stars * dir;
	float density = 180.0;
	vec3 cell = floor(d * density);
	float sum = 0.0;
	for (int i = 0; i < 2; i++) {
		vec3 c = cell + vec3(float(i) * 0.5);
		float present = step(0.9965, hash13(c));
		vec3 center = (floor(d * density) + hash33(c)) / density;
		float dist = length(d - normalize(center)) * density;
		float brightness = mix(0.4, 3.0, pow(hash13(c + 7.1), 6.0));
		sum += present * brightness * smoothstep(0.55, 0.0, dist);
	}
	return sum;
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	float h = dot(dir, u_up);
	float sun_h = dot(u_sun, u_up);
	float day = smoothstep(-0.16, 0.22, sun_h);
	float toward_sun = max(dot(dir, u_sun), 0.0);

	float horizon = pow(1.0 - clamp(h, 0.0, 1.0), 3.0);
	vec3 day_color = mix(u_day_zenith, u_day_horizon, horizon);
	vec3 night_color = mix(u_night_zenith, u_night_horizon, horizon);
	vec3 color = mix(night_color, day_color, day);

	// Dawn and dusk: an orange band along the horizon, strongest towards the sun.
	float twilight = 1.0 - smoothstep(0.0, 0.32, abs(sun_h + 0.03));
	color += u_sunset * twilight * horizon * (0.25 + 1.1 * pow(toward_sun, 3.0));

	// Below the horizon the atmosphere fades into a hazy ground colour.
	// Beyond the rendered terrain the haze of the horizon continues downwards (no dark band under the horizon).
	vec3 ground = mix(u_ground, mix(night_color, day_color, day), 0.75) * mix(0.05, 1.0, day) + u_sunset * twilight * 0.1;
	color = mix(color, ground, smoothstep(0.0, -0.12, h) * u_atmosphere);

	// Glow around the sun and the sun disk (hidden behind the planet while inside the atmosphere).
	float above = mix(1.0, smoothstep(-0.03, 0.01, h), u_atmosphere);
	color += vec3(1.0, 0.78, 0.5) * pow(toward_sun, 64.0) * 0.6 * (0.3 + day) * above * u_atmosphere;
	if (!AT_CUBEMAP_PASS) {
		float disk = smoothstep(0.99955, 0.99975, toward_sun);
		color += vec3(1.0, 0.94, 0.82) * disk * 25.0 * above;
	}

	// Space: black with a thin glow left.
	color = mix(vec3(0.0005, 0.0007, 0.0015), color, u_atmosphere);

	if (!AT_CUBEMAP_PASS) {
		float star_visibility = max(1.0 - day, 1.0 - u_atmosphere) * mix(1.0, smoothstep(-0.02, 0.08, h), u_atmosphere);
		color += vec3(0.9, 0.93, 1.0) * stars(dir) * star_visibility;
	}

	COLOR = color;
}
