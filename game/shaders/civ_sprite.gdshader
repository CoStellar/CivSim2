// Sprites with the civilization colour (GDD 12.1): the reserved magentas #FF00FF, #C000C0 and #800080 become the
// light, middle and dark shade of the colour given as the draw call's modulate. Other pixels keep their colour.
// layer_opacity fades the whole layer (layer panel).
shader_type canvas_item;

uniform float layer_opacity = 1.0;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	vec3 civ = COLOR.rgb;
	vec3 c = tex.rgb;
	bool magenta = tex.g < 0.02 && abs(tex.r - tex.b) < 0.02 && tex.r > 0.4;
	if (magenta) {
		if (tex.r > 0.9) {
			c = mix(civ, vec3(1.0), 0.25);
		} else if (tex.r > 0.6) {
			c = civ;
		} else {
			c = civ * 0.6;
		}
	}
	COLOR = vec4(c, tex.a * COLOR.a * layer_opacity);
}
