shader_type spatial;
// Planet terrain (K2-08, dressed up later): Voxel Tools Transvoxel mesh with one texture index per voxel (TEXTURES_SINGLE_S4),
// triplanar sampling of a Texture2DArray generated in code (PlanetTerrainMaterial). Vertex data layout: docs of Voxel Tools,
// "smooth_terrain": CUSTOM0 = transition smoothing, CUSTOM1.x = 4 packed texture indices, CUSTOM1.y = 4 packed weights.
// The natural surface (voxel material "dirt") is painted from height and slope only: beach near the sea, grass and meadow on
// gentle slopes, bare rock on steep ones, snow above the snow line. Purely visual; the voxel data never changes.

uniform sampler2DArray u_textures : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float u_texture_scale = 0.25;
// Share of the impostor in the cross-fade (K5-22 decision 13): the terrain discards the Bayer pixels the impostor draws.
uniform float u_blend = 0.0;

// Planet frame: the terrain node sits at the planet centre (PlanetLook holds the heights, relative to the nominal radius).
uniform vec3 u_planet_center = vec3(0.0);
uniform float u_planet_radius = 4000.0;
uniform float u_sea_level = -42.0;
uniform float u_beach_height = 3.5;
uniform float u_snow_line = 135.0;

// Assigned by the voxel engine: neighbours of lower detail (layout 00000000 00000000 00000000 00xxyyzz).
uniform int u_transition_mask;

const float LAYER_STONE = 0.0;
const float LAYER_DIRT = 1.0;
const float LAYER_SAND = 2.0;
const float LAYER_GRASS = 5.0;
const float LAYER_SNOW = 6.0;
const float LAYER_CLIFF = 7.0;
const float LAYER_MEADOW = 8.0;

varying vec4 v_indices;
varying vec4 v_weights;
varying vec3 v_normal;
varying vec3 v_pos;

float get_transvoxel_secondary_factor(int idata) {
	int transition_mask = u_transition_mask & 0xff;
	int cell_border_mask = idata & 63;
	int vertex_border_mask = (idata >> 8) & 63;
	int m = transition_mask & cell_border_mask;
	float t = float(m != 0);
	t *= float((vertex_border_mask & ~transition_mask) == 0);
	return t;
}

vec3 get_transvoxel_position(vec3 vertex_pos, vec4 fdata) {
	int idata = floatBitsToInt(fdata.a);
	float secondary_factor = get_transvoxel_secondary_factor(idata);
	vec3 pos = mix(vertex_pos, fdata.xyz, secondary_factor);
	// Transition vertices of inactive transitions collapse into degenerate triangles.
	int itransition = (idata >> 16) & 0xff;
	float transition_cull = float(itransition == 0 || (itransition & u_transition_mask) != 0);
	return pos * transition_cull;
}

vec4 decode_8bit_vec4(float v) {
	uint i = floatBitsToUint(v);
	return vec4(
		float(i & uint(0xff)),
		float((i >> uint(8)) & uint(0xff)),
		float((i >> uint(16)) & uint(0xff)),
		float((i >> uint(24)) & uint(0xff)));
}

// 4 x 4 Bayer threshold at a pixel, the same as ImpostorBlend.ImpostorDraws (K5-22 decision 13).
float bayer4(vec2 frag) {
	const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	ivec2 p = ivec2(floor(frag)) & ivec2(3);
	return (m[p.y * 4 + p.x] + 0.5) / 16.0;
}

vec3 get_triplanar_blend(vec3 world_normal) {
	vec3 blending = pow(abs(world_normal), vec3(4.0));
	blending = max(blending, vec3(0.00001));
	return blending / (blending.x + blending.y + blending.z);
}

vec3 sample_triplanar(vec3 pos, vec3 blend, float index) {
	vec3 xaxis = texture(u_textures, vec3(pos.yz, index)).rgb;
	vec3 yaxis = texture(u_textures, vec3(pos.xz, index)).rgb;
	vec3 zaxis = texture(u_textures, vec3(pos.xy, index)).rgb;
	return xaxis * blend.x + yaxis * blend.y + zaxis * blend.z;
}

// Two scales of the same layer hide the tiling of the 64 m texture period.
vec3 sample_layer(vec3 pos, vec3 blend, float index) {
	vec3 near = sample_triplanar(pos, blend, index);
	vec3 far = sample_triplanar(pos * 0.083, blend, index);
	return near * mix(vec3(0.75), far * 1.35, 0.5);
}

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
	for (int i = 0; i < 4; i++) {
		sum += value_noise(x) * amplitude;
		x = x * 2.03 + vec3(17.1, 3.7, 9.2);
		amplitude *= 0.5;
	}
	return sum;
}

float layer_weight(vec4 indices, vec4 weights, float layer) {
	vec4 match = vec4(lessThan(abs(indices - vec4(layer)), vec4(0.5)));
	return dot(match, weights);
}

void vertex() {
	VERTEX = get_transvoxel_position(VERTEX, CUSTOM0);
	v_indices = decode_8bit_vec4(CUSTOM1.x);
	v_weights = decode_8bit_vec4(CUSTOM1.y) / 255.0;
	// K5-22 decision 14: triplanar coordinates in model space, never world or view. A mesh block of Voxel Tools sits at its origin in
	// the terrain (identity basis, origin a multiple of 16 m), the terrain at the planet centre (planet frame), so the block-local vertex
	// differs from the planet-frame one by a multiple of 16 m = 4 texture periods at u_texture_scale 0.25: seamless, exact far from
	// the world origin (no float jitter about 110 km away).
	v_pos = VERTEX;
	v_normal = normalize(NORMAL);
}

void fragment() {
	if (bayer4(FRAGCOORD.xy) < u_blend) {
		discard;
	}
	vec3 normal = normalize(v_normal);
	vec3 blend = get_triplanar_blend(normal);
	vec3 p = v_pos * u_texture_scale;
	vec4 weights = v_weights;
	weights /= (weights.x + weights.y + weights.z + weights.w + 0.00001);

	vec3 voxel_color = sample_layer(p, blend, v_indices.x) * weights.x
		+ sample_layer(p, blend, v_indices.y) * weights.y
		+ sample_layer(p, blend, v_indices.z) * weights.z
		+ sample_layer(p, blend, v_indices.w) * weights.w;

	vec3 rel = v_pos - u_planet_center;
	float radius = length(rel);
	vec3 up = rel / max(radius, 1.0);
	float height = radius - u_planet_radius;
	float flatness = dot(normal, up);

	// Large-scale variation: biomes of meadow and lush grass, uneven snow and beach borders.
	float macro = fbm(v_pos * 0.0035);
	float detail = value_noise(v_pos * 0.35);
	// Coarse LOD meshes average the thin dirt layer into the stone below, so far away stone counts as natural surface too;
	// near the camera stone stays stone (dug pits, caves).
	float far = smoothstep(250.0, 600.0, length(v_pos - CAMERA_POSITION_WORLD));
	float dirt = clamp(layer_weight(v_indices, weights, LAYER_DIRT) + layer_weight(v_indices, weights, LAYER_STONE) * far, 0.0, 1.0);
	float sand = layer_weight(v_indices, weights, LAYER_SAND);
	float natural = clamp(dirt + sand, 0.0, 1.0);

	float above_sea = height - u_sea_level;
	float beach = dirt * (1.0 - smoothstep(u_beach_height - 1.0, u_beach_height + 1.5, above_sea + (macro - 0.5) * 3.0))
		* smoothstep(0.55, 0.75, flatness);
	float grass = dirt * smoothstep(0.6, 0.72, flatness + (detail - 0.5) * 0.06) * (1.0 - beach);
	float cliff = natural * (1.0 - smoothstep(0.45, 0.62, flatness + (macro - 0.5) * 0.12));
	float snow = natural * smoothstep(u_snow_line - 25.0, u_snow_line + 8.0, height + (macro - 0.5) * 60.0)
		* smoothstep(0.42, 0.68, flatness);

	vec3 grass_color = mix(
		sample_layer(p, blend, LAYER_GRASS),
		sample_layer(p, blend, LAYER_MEADOW),
		smoothstep(0.42, 0.62, macro));
	// Grass gets drier and sparser towards the snow line.
	grass_color = mix(grass_color, sample_layer(p, blend, LAYER_MEADOW), smoothstep(u_snow_line - 70.0, u_snow_line, height) * 0.7);

	vec3 color = voxel_color;
	color = mix(color, sample_layer(p, blend, LAYER_SAND), beach);
	color = mix(color, grass_color, grass);
	color = mix(color, sample_layer(p, blend, LAYER_CLIFF), cliff);
	color = mix(color, sample_layer(p, blend, LAYER_SNOW), snow);

	// Wet sand and mud right at the water line, darker sea floor.
	float wet = natural * (1.0 - smoothstep(0.0, 0.8, above_sea)) ;
	color *= mix(1.0, 0.72, wet);
	color *= mix(1.0, 0.85, smoothstep(0.42, 0.75, macro) * grass);

	ALBEDO = color;
	ROUGHNESS = mix(0.95, 0.55, snow);
	SPECULAR = mix(0.3, 0.5, snow);
}
