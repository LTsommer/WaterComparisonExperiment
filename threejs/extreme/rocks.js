/*
 * Procedural hero-rock generator for the extreme Three.js ocean scene.
 *
 * The expensive work in this file happens once, when the rock is created. The
 * resulting mesh is ordinary static BufferGeometry; there is no per-frame CPU
 * deformation or noise evaluation. A small MeshStandardMaterial hook supplies
 * the wet waterline, bedding and crevice colour response while retaining Three's
 * normal lighting, shadows, fog and environment-map path.
 */

const TAU = Math.PI * 2;

const clamp = (value, minimum, maximum) => Math.min(maximum, Math.max(minimum, value));

function smoothstep(edge0, edge1, value) {
  const t = clamp((value - edge0) / (edge1 - edge0), 0, 1);
  return t * t * (3 - 2 * t);
}

function seedToUint(seed) {
  const text = String(seed ?? 1);
  let hash = 2166136261;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  hash ^= hash >>> 16;
  hash = Math.imul(hash, 0x7feb352d);
  hash ^= hash >>> 15;
  hash = Math.imul(hash, 0x846ca68b);
  hash ^= hash >>> 16;
  return hash >>> 0;
}

function mulberry32(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let value = state;
    value = Math.imul(value ^ (value >>> 15), value | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
  };
}

function createPerlinNoise(seed) {
  const random = mulberry32(seed);
  const source = new Uint8Array(256);
  const permutation = new Uint8Array(512);
  for (let index = 0; index < 256; index += 1) source[index] = index;

  for (let index = 255; index > 0; index -= 1) {
    const swapIndex = Math.floor(random() * (index + 1));
    const temporary = source[index];
    source[index] = source[swapIndex];
    source[swapIndex] = temporary;
  }
  for (let index = 0; index < 512; index += 1) permutation[index] = source[index & 255];

  const fade = (value) => value * value * value * (value * (value * 6 - 15) + 10);
  const lerp = (start, end, amount) => start + (end - start) * amount;
  const gradient = (hash, x, y, z) => {
    const code = hash & 15;
    const first = code < 8 ? x : y;
    const second = code < 4 ? y : code === 12 || code === 14 ? x : z;
    return ((code & 1) === 0 ? first : -first) + ((code & 2) === 0 ? second : -second);
  };

  return (x, y, z) => {
    const floorX = Math.floor(x);
    const floorY = Math.floor(y);
    const floorZ = Math.floor(z);
    const cellX = floorX & 255;
    const cellY = floorY & 255;
    const cellZ = floorZ & 255;
    const localX = x - floorX;
    const localY = y - floorY;
    const localZ = z - floorZ;
    const u = fade(localX);
    const v = fade(localY);
    const w = fade(localZ);

    const a = permutation[cellX] + cellY;
    const aa = permutation[a] + cellZ;
    const ab = permutation[a + 1] + cellZ;
    const b = permutation[cellX + 1] + cellY;
    const ba = permutation[b] + cellZ;
    const bb = permutation[b + 1] + cellZ;

    return lerp(
      lerp(
        lerp(gradient(permutation[aa], localX, localY, localZ), gradient(permutation[ba], localX - 1, localY, localZ), u),
        lerp(gradient(permutation[ab], localX, localY - 1, localZ), gradient(permutation[bb], localX - 1, localY - 1, localZ), u),
        v,
      ),
      lerp(
        lerp(gradient(permutation[aa + 1], localX, localY, localZ - 1), gradient(permutation[ba + 1], localX - 1, localY, localZ - 1), u),
        lerp(gradient(permutation[ab + 1], localX, localY - 1, localZ - 1), gradient(permutation[bb + 1], localX - 1, localY - 1, localZ - 1), u),
        v,
      ),
      w,
    );
  };
}

function fractalNoise(noise, x, y, z, octaves = 4, lacunarity = 2.03, gain = 0.5) {
  let sum = 0;
  let amplitude = 1;
  let normalizer = 0;
  let frequency = 1;
  for (let octave = 0; octave < octaves; octave += 1) {
    sum += noise(x * frequency, y * frequency, z * frequency) * amplitude;
    normalizer += amplitude;
    frequency *= lacunarity;
    amplitude *= gain;
  }
  return sum / normalizer;
}

// Ridged multifractal noise makes sharp erosion crests without turning the
// silhouette into uniformly distributed random spikes.
function ridgedMultifractal(noise, x, y, z, octaves = 5) {
  let sum = 0;
  let amplitude = 0.55;
  let frequency = 1;
  let weight = 1;
  let normalizer = 0;
  for (let octave = 0; octave < octaves; octave += 1) {
    let ridge = 1 - Math.abs(noise(x * frequency, y * frequency, z * frequency));
    ridge *= ridge;
    ridge *= weight;
    weight = clamp(ridge * 2.35, 0, 1);
    sum += ridge * amplitude;
    normalizer += amplitude;
    frequency *= 2.11;
    amplitude *= 0.52;
  }
  return sum / normalizer;
}

function readVector3(value, fallback) {
  if (typeof value === 'number') return [value, value, value];
  if (Array.isArray(value)) return [value[0] ?? fallback[0], value[1] ?? fallback[1], value[2] ?? fallback[2]];
  if (value && typeof value === 'object') return [value.x ?? fallback[0], value.y ?? fallback[1], value.z ?? fallback[2]];
  return [...fallback];
}

function readEuler(value) {
  if (typeof value === 'number') return [0, value, 0, 'XYZ'];
  if (Array.isArray(value)) return [value[0] ?? 0, value[1] ?? 0, value[2] ?? 0, value[3] ?? 'XYZ'];
  if (value && typeof value === 'object') return [value.x ?? 0, value.y ?? 0, value.z ?? 0, value.order ?? 'XYZ'];
  return [0, 0, 0, 'XYZ'];
}

function makeCrackPlanes(random, count) {
  return Array.from({ length: count }, (_, index) => {
    // Mostly vertical fracture planes, with enough tilt to avoid parallel cuts.
    const angle = random() * TAU;
    let x = Math.cos(angle);
    let y = (random() - 0.5) * 0.72;
    let z = Math.sin(angle);
    const inverseLength = 1 / Math.hypot(x, y, z);
    x *= inverseLength;
    y *= inverseLength;
    z *= inverseLength;
    return {
      x,
      y,
      z,
      offset: (random() - 0.5) * 0.4,
      width: 0.025 + random() * 0.035,
      noiseOffset: 31.7 + index * 17.13 + random() * 9,
    };
  });
}

function makeCutPlanes(random, radius, count) {
  return Array.from({ length: count }, (_, index) => {
    const azimuth = random() * TAU;
    // Most cuts attack the silhouette from a side; one can expose a slanted top.
    const vertical = index === count - 1
      ? 0.28 + random() * 0.48
      : (random() - 0.5) * 0.58;
    let x = Math.cos(azimuth);
    let y = vertical;
    let z = Math.sin(azimuth);
    const inverseLength = 1 / Math.hypot(x, y, z);
    x *= inverseLength;
    y *= inverseLength;
    z *= inverseLength;
    return {
      x,
      y,
      z,
      distance: radius * (0.60 + random() * 0.23),
      hardness: 0.78 + random() * 0.18,
    };
  });
}

function addStaticDebris(THREE, parent, material, random, radius, shape, amount) {
  let triangleCount = 0;
  for (let fragmentIndex = 0; fragmentIndex < amount; fragmentIndex += 1) {
    const fragmentRadius = radius * (0.065 + random() * 0.075);
    const geometry = new THREE.DodecahedronGeometry(fragmentRadius, 0);
    const positions = geometry.getAttribute('position');
    const data = new Float32Array(positions.count * 3);
    const stretch = [0.75 + random() * 0.55, 0.55 + random() * 0.55, 0.75 + random() * 0.55];
    for (let index = 0; index < positions.count; index += 1) {
      const jitter = 0.80 + random() * 0.32;
      positions.setXYZ(
        index,
        positions.getX(index) * stretch[0] * jitter,
        positions.getY(index) * stretch[1] * jitter,
        positions.getZ(index) * stretch[2] * jitter,
      );
      data[index * 3] = random() * 0.28;
      data[index * 3 + 1] = 0.25 + random() * 0.65;
      data[index * 3 + 2] = random();
    }
    positions.needsUpdate = true;
    geometry.setAttribute('aRockData', new THREE.BufferAttribute(data, 3));
    geometry.computeVertexNormals();
    geometry.computeBoundingSphere();

    const fragment = new THREE.Mesh(geometry, material);
    const angle = random() * TAU;
    const distance = radius * (0.72 + random() * 0.42);
    fragment.position.set(
      Math.cos(angle) * distance * shape[0],
      -radius * shape[1] * (0.67 + random() * 0.08),
      Math.sin(angle) * distance * shape[2],
    );
    fragment.rotation.set(random() * TAU, random() * TAU, random() * TAU);
    fragment.castShadow = true;
    fragment.receiveShadow = true;
    fragment.name = `Static fracture fragment ${fragmentIndex + 1}`;
    parent.add(fragment);
    triangleCount += geometry.index ? geometry.index.count / 3 : positions.count / 3;
  }
  return triangleCount;
}

function createRockMaterial(THREE, options, radius) {
  const dryColor = new THREE.Color(options.color ?? options.dryColor ?? '#475052');
  const wetColor = new THREE.Color(options.wetColor ?? '#101d20');
  const algaeColor = new THREE.Color(options.algaeColor ?? '#263f37');
  const wetFeather = Math.max(0.001, options.wetFeather ?? radius * 0.18);

  const rockUniforms = {
    uRockWetLine: { value: options.wetLine ?? 0 },
    uRockWetFeather: { value: wetFeather },
    uRockWetColor: { value: wetColor },
    uRockAlgaeColor: { value: algaeColor },
    uRockWetRoughness: { value: clamp(options.wetRoughness ?? 0.24, 0.04, 1) },
  };

  const material = new THREE.MeshStandardMaterial({
    color: dryColor,
    roughness: clamp(options.roughness ?? 0.86, 0.04, 1),
    metalness: clamp(options.metalness ?? 0.0, 0, 1),
    envMapIntensity: options.envMapIntensity ?? 1.05,
    flatShading: options.flatShading ?? false,
    dithering: true,
  });

  material.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, rockUniforms);
    shader.vertexShader = shader.vertexShader
      .replace(
        '#include <common>',
        `#include <common>
attribute vec3 aRockData;
varying vec3 vRockData;
varying vec3 vRockWorldPosition;`,
      )
      .replace(
        '#include <begin_vertex>',
        `#include <begin_vertex>
vRockData = aRockData;
vRockWorldPosition = (modelMatrix * vec4(transformed, 1.0)).xyz;`,
      );

    shader.fragmentShader = shader.fragmentShader
      .replace(
        '#include <common>',
        `#include <common>
uniform float uRockWetLine;
uniform float uRockWetFeather;
uniform float uRockWetRoughness;
uniform vec3 uRockWetColor;
uniform vec3 uRockAlgaeColor;
varying vec3 vRockData;
varying vec3 vRockWorldPosition;`,
      )
      .replace(
        'vec4 diffuseColor = vec4( diffuse, opacity );',
        `vec4 diffuseColor = vec4(diffuse, opacity);
// Interpolated construction-time noise makes the waterline irregular while
// remaining completely static on the CPU.
float rockLineOffset = (vRockData.z - 0.5) * uRockWetFeather * 1.10
  + (vRockData.y - 0.5) * uRockWetFeather * 0.25;
float rockWetness = 1.0 - smoothstep(
  uRockWetLine - uRockWetFeather,
  uRockWetLine + uRockWetFeather,
  vRockWorldPosition.y + rockLineOffset
);
float rockBandDistance = abs(vRockWorldPosition.y - uRockWetLine)
  / max(uRockWetFeather, 0.0001);
float rockTideBand = (1.0 - smoothstep(0.10, 1.65, rockBandDistance))
  * (0.35 + vRockData.z * 0.65);
float rockLayerTone = mix(0.74, 1.13, vRockData.y);
float rockGrainTone = mix(0.88, 1.11, vRockData.z);
vec3 rockDry = diffuseColor.rgb * rockLayerTone * rockGrainTone;
rockDry *= mix(1.0, 0.16, pow(clamp(vRockData.x, 0.0, 1.0), 1.18));
vec3 rockWet = mix(rockDry * vec3(0.31, 0.39, 0.42), uRockWetColor, 0.48);
vec3 rockColored = mix(rockDry, rockWet, rockWetness);
rockColored = mix(rockColored, uRockAlgaeColor * rockLayerTone, rockTideBand * 0.32);
diffuseColor.rgb = rockColored;`,
      )
      .replace(
        '#include <roughnessmap_fragment>',
        `#include <roughnessmap_fragment>
roughnessFactor = clamp(
  mix(roughnessFactor, uRockWetRoughness, rockWetness * 0.92) + vRockData.x * 0.07,
  0.04,
  1.0
);`,
      );

    material.userData.shader = shader;
  };

  // All options that vary per rock are uniforms, so instances can safely reuse
  // the same compiled WebGL program.
  material.customProgramCacheKey = () => 'rugged-rock-standard-v2';
  material.userData.rockUniforms = rockUniforms;
  return material;
}

/**
 * Create a deterministic, statically deformed coastal rock.
 *
 * @param {typeof import('three')} THREE Three.js module namespace.
 * @param {object} options
 * @param {number|string} [options.seed=1] Reproducible rock identity.
 * @param {number} [options.radius=1] Undeformed local-space radius.
 * @param {number} [options.detail=7] Icosahedron subdivision, clamped to 2..10.
 * @param {number|Array|object} [options.position] World position.
 * @param {number|Array|object} [options.scale] Object scale.
 * @param {number|Array|object} [options.rotation] Euler rotation (number means Y).
 * @param {number} [options.wetLine=0] World-space height of the waterline.
 * @returns {import('three').Mesh}
 */
export function createRuggedRock(THREE, options = {}) {
  if (!THREE?.IcosahedronGeometry || !THREE?.MeshStandardMaterial) {
    throw new TypeError('createRuggedRock requires a valid Three.js module namespace.');
  }

  const seed = seedToUint(options.seed ?? 1);
  const random = mulberry32(seed);
  const noise = createPerlinNoise(seed ^ 0xa511e9b3);
  const radius = Math.max(0.01, Number(options.radius ?? 1));
  // PolyhedronGeometry's detail grows quadratically (not exponentially), so 7
  // gives a close-up-friendly 1,280 triangles while remaining inexpensive.
  const detail = clamp(Math.round(options.detail ?? 7), 2, 10);
  const ruggedness = clamp(options.ruggedness ?? 0.24, 0.04, 0.42);
  const strataStrength = clamp(options.strataStrength ?? 0.075, 0, 0.2);
  const crackStrength = clamp(options.crackStrength ?? 0.065, 0, 0.16);
  const domainWarp = clamp(options.domainWarp ?? 0.34, 0, 0.75);
  const geometry = new THREE.IcosahedronGeometry(1, detail);
  const positions = geometry.getAttribute('position');
  const rockData = new Float32Array(positions.count * 3);
  const crackPlanes = makeCrackPlanes(random, clamp(Math.round(options.cracks ?? 3), 1, 6));
  const cutPlanes = makeCutPlanes(random, radius, clamp(Math.round(options.cuts ?? 3), 1, 5));

  const generatedShape = [0.93 + random() * 0.24, 0.80 + random() * 0.22, 0.91 + random() * 0.25];
  const shape = readVector3(options.shape, generatedShape).map((axis) => Math.max(0.2, axis));
  const leanX = (random() - 0.5) * 0.16;
  const leanZ = (random() - 0.5) * 0.16;
  const twist = (random() - 0.5) * 0.24;
  const strataFrequency = 5.5 + random() * 3.5;
  const phase = random() * TAU;

  for (let index = 0; index < positions.count; index += 1) {
    let directionX = positions.getX(index);
    let directionY = positions.getY(index);
    let directionZ = positions.getZ(index);
    const inverseLength = 1 / Math.hypot(directionX, directionY, directionZ);
    directionX *= inverseLength;
    directionY *= inverseLength;
    directionZ *= inverseLength;

    const baseX = directionX * 1.24;
    const baseY = directionY * 1.24;
    const baseZ = directionZ * 1.24;
    const warpX = fractalNoise(noise, baseX + 13.1, baseY - 7.7, baseZ + 3.9, 3);
    const warpY = fractalNoise(noise, baseX - 5.4, baseY + 19.2, baseZ - 11.3, 3);
    const warpZ = fractalNoise(noise, baseX + 8.8, baseY + 2.1, baseZ + 23.5, 3);
    const warpedX = baseX + warpX * domainWarp;
    const warpedY = baseY + warpY * domainWarp;
    const warpedZ = baseZ + warpZ * domainWarp;

    const macro = fractalNoise(noise, warpedX * 0.82, warpedY * 0.82, warpedZ * 0.82, 4, 2.03, 0.51);
    const ridges = ridgedMultifractal(noise, warpedX * 1.58, warpedY * 1.58, warpedZ * 1.58, 5);
    const beddingWarp = fractalNoise(noise, warpedX * 1.7 + 41.2, warpedY * 1.7, warpedZ * 1.7 - 16.3, 3);
    const beddingPhase = (directionY * 0.5 + 0.5) * strataFrequency * TAU + beddingWarp * 2.3 + phase;
    const beddingWave = Math.sin(beddingPhase);
    const ledge = Math.pow(Math.max(0, beddingWave), 7);

    let crack = 0;
    for (const plane of crackPlanes) {
      const fractureNoise = noise(
        warpedX * 2.6 + plane.noiseOffset,
        warpedY * 2.6 - plane.noiseOffset * 0.37,
        warpedZ * 2.6 + plane.noiseOffset * 0.61,
      );
      const distance = Math.abs(
        directionX * plane.x + directionY * plane.y + directionZ * plane.z
          + plane.offset + fractureNoise * 0.105,
      );
      const fissure = 1 - smoothstep(plane.width, plane.width * 2.9, distance);
      const pathGate = smoothstep(
        -0.34,
        0.18,
        noise(warpedX * 1.31 - plane.noiseOffset, warpedY * 1.31 + 4.7, warpedZ * 1.31),
      );
      crack = Math.max(crack, fissure * pathGate);
    }

    const erosion = ruggedness * (macro * 0.70 + (ridges - 0.48) * 0.58);
    const radialDistance = radius * Math.max(0.56, 1 + erosion + ledge * strataStrength - crack * crackStrength);
    const twistAngle = twist * directionY;
    const cosine = Math.cos(twistAngle);
    const sine = Math.sin(twistAngle);
    const twistedX = directionX * cosine - directionZ * sine;
    const twistedZ = directionX * sine + directionZ * cosine;
    let x = twistedX * radialDistance * shape[0];
    let y = directionY * radialDistance * shape[1];
    let z = twistedZ * radialDistance * shape[2];
    x += y * leanX;
    z += y * leanZ;

    // Unlike radial noise, these one-sided planar cuts create recognisable
    // broken faces and hard silhouette changes. A little relief is retained on
    // each cut so it does not resemble a mathematically perfect clipping plane.
    let cutEdge = 0;
    for (const cut of cutPlanes) {
      const projection = x * cut.x + y * cut.y + z * cut.z;
      const excess = projection - cut.distance;
      if (excess > 0) {
        const retainedRelief = excess * (1 - cut.hardness);
        const pushBack = excess - retainedRelief;
        x -= cut.x * pushBack;
        y -= cut.y * pushBack;
        z -= cut.z * pushBack;
      }
      const edgeWidth = radius * 0.055;
      cutEdge = Math.max(cutEdge, 1 - smoothstep(edgeWidth, edgeWidth * 2.5, Math.abs(excess)));
    }

    // A compressed underside seats the boulder in sand instead of leaving a
    // conspicuously spherical lower silhouette.
    const floorY = -radius * shape[1] * 0.76;
    if (y < floorY) y = floorY + (y - floorY) * 0.16;

    positions.setXYZ(index, x, y, z);
    const layerShade = clamp(0.5 + beddingWave * 0.24 + beddingWarp * 0.22, 0, 1);
    const grain = clamp(
      0.5 + fractalNoise(noise, warpedX * 4.8 + 5.1, warpedY * 4.8 - 2.7, warpedZ * 4.8 + 9.3, 3) * 0.72,
      0,
      1,
    );
    rockData[index * 3] = Math.max(crack, cutEdge * 0.34);
    rockData[index * 3 + 1] = layerShade;
    rockData[index * 3 + 2] = grain;
  }

  positions.needsUpdate = true;
  geometry.setAttribute('aRockData', new THREE.BufferAttribute(rockData, 3));
  geometry.computeVertexNormals();
  geometry.computeBoundingBox();
  geometry.computeBoundingSphere();
  geometry.name = `Rugged rock geometry · ${String(options.seed ?? 1)}`;

  const material = createRockMaterial(THREE, options, radius);
  const mesh = new THREE.Mesh(geometry, material);
  const position = readVector3(options.position, [0, 0, 0]);
  const scale = readVector3(options.scale, [1, 1, 1]);
  const rotation = readEuler(options.rotation);
  mesh.position.set(position[0], position[1], position[2]);
  mesh.scale.set(scale[0], scale[1], scale[2]);
  mesh.rotation.set(rotation[0], rotation[1], rotation[2], rotation[3]);
  mesh.castShadow = options.castShadow ?? true;
  mesh.receiveShadow = options.receiveShadow ?? true;
  mesh.name = options.name ?? `Rugged coastal rock · ${String(options.seed ?? 1)}`;
  mesh.userData.proceduralRock = {
    seed,
    sourceSeed: options.seed ?? 1,
    staticGeometry: true,
    triangles: geometry.index ? geometry.index.count / 3 : positions.count / 3,
  };
  mesh.userData.setWetLine = (worldHeight) => {
    material.userData.rockUniforms.uRockWetLine.value = worldHeight;
  };

  const debrisCount = clamp(Math.round(options.debris ?? 2), 0, 6);
  mesh.userData.proceduralRock.triangles += addStaticDebris(
    THREE,
    mesh,
    material,
    random,
    radius,
    shape,
    debrisCount,
  );

  return mesh;
}
