using System.Globalization;
using System.Text;
using System.Text.Json;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Exporters;

/// <summary>
/// Options configuring standalone 3D WebGL HTML viewer export for IFC models.
/// </summary>
public sealed class IfcHtmlViewerOptions
{
    public string Title { get; set; } = "IFC 3D Model";
    public bool IncludeProperties { get; set; } = true;
    public bool IncludeQuantities { get; set; } = true;
    public bool IncludeSpatialTree { get; set; } = true;
    public bool IncludeSectioning { get; set; } = true;
    public int MaxMeshVertices { get; set; } = 500000;
}

/// <summary>
/// Exports an <see cref="IfcModel"/> to an interactive, self-contained single-file
/// 3D WebGL HTML viewer (.html) with zero external script or server dependencies.
/// </summary>
public static class IfcHtmlViewerExporter
{
    /// <summary>
    /// Exports an <see cref="IfcModel"/> to a standalone 3D WebGL HTML file.
    /// </summary>
    public static void Save(string path, IfcModel model, IfcHtmlViewerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(model);

        options ??= new IfcHtmlViewerOptions { Title = !string.IsNullOrEmpty(model.ProjectName) ? model.ProjectName : Path.GetFileNameWithoutExtension(path) };
        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, Encoding.UTF8);
        Write(writer, model, options);
    }

    /// <summary>
    /// Writes the standalone 3D WebGL HTML viewer to a <see cref="TextWriter"/>.
    /// </summary>
    public static void Write(TextWriter writer, IfcModel model, IfcHtmlViewerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(model);
        options ??= new IfcHtmlViewerOptions();

        string jsonPayload = BuildJsonPayload(model, options);

        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html lang=\"en\">");
        writer.WriteLine("<head>");
        writer.WriteLine("<meta charset=\"UTF-8\">");
        writer.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        writer.Write("<title>");
        writer.Write(System.Net.WebUtility.HtmlEncode(options.Title));
        writer.WriteLine("</title>");
        writer.WriteLine(GetCss());
        writer.WriteLine("</head>");
        writer.WriteLine("<body>");
        writer.WriteLine(GetHeaderAndLayout(options.Title));
        writer.WriteLine("<script id=\"ifc-data\" type=\"application/json\">");
        writer.WriteLine(jsonPayload);
        writer.WriteLine("</script>");
        writer.WriteLine(GetViewerScript());
        writer.WriteLine("</body>");
        writer.WriteLine("</html>");
    }

    private static string GetCss()
    {
        return """
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #1a1a1a; color: #e0e0e0; overflow: hidden; height: 100vh; display: flex; flex-direction: column; }
header { background: #111; border-bottom: 1px solid #333; padding: 10px 16px; display: flex; align-items: center; justify-content: space-between; z-index: 10; }
header h1 { font-size: 15px; font-weight: 600; color: #fff; letter-spacing: 0.5px; }
.toolbar { display: flex; gap: 8px; }
.btn { background: #282828; color: #ccc; border: 1px solid #444; padding: 5px 12px; border-radius: 4px; font-size: 12px; cursor: pointer; transition: all 0.15s ease; }
.btn:hover { background: #3a3a3a; color: #fff; border-color: #666; }
.container { flex: 1; display: flex; position: relative; overflow: hidden; }
.left-bar { width: 280px; background: #141414; border-right: 1px solid #2a2a2a; display: flex; flex-direction: column; z-index: 5; }
.tab-row { display: flex; border-bottom: 1px solid #2a2a2a; background: #181818; }
.tab-btn { flex: 1; padding: 8px 4px; font-size: 11px; text-transform: uppercase; font-weight: 600; background: none; border: none; color: #777; cursor: pointer; border-bottom: 2px solid transparent; }
.tab-btn.active { color: #00adb5; border-bottom: 2px solid #00adb5; }
.panel-content { flex: 1; overflow-y: auto; padding: 10px; font-size: 12px; }
.sidebar { width: 300px; background: #141414; border-left: 1px solid #2a2a2a; display: flex; flex-direction: column; z-index: 5; }
.panel-header { padding: 10px 14px; font-size: 11px; text-transform: uppercase; font-weight: 700; color: #888; border-bottom: 1px solid #222; }
#viewport-container { flex: 1; position: relative; background: radial-gradient(circle at center, #2b3038 0%, #16181c 100%); overflow: hidden; }
canvas#glcanvas { width: 100%; height: 100%; display: block; }
.hud-stats { position: absolute; bottom: 12px; left: 14px; font-size: 11px; color: rgba(255,255,255,0.45); pointer-events: none; font-family: monospace; z-index: 5; }
.section-panel { position: absolute; top: 12px; left: 12px; background: rgba(20, 20, 20, 0.9); border: 1px solid #333; border-radius: 6px; padding: 12px; font-size: 12px; z-index: 6; width: 210px; }
.section-panel label { display: block; margin: 6px 0 2px; font-weight: 600; color: #bbb; }
.section-panel input[type=range] { width: 100%; accent-color: #00adb5; }
.prop-card { background: #1e1e1e; border: 1px solid #2d2d2d; border-radius: 4px; margin-bottom: 10px; overflow: hidden; }
.prop-card-title { background: #252525; padding: 6px 10px; font-weight: 600; font-size: 11px; color: #00adb5; border-bottom: 1px solid #333; }
.prop-row { display: flex; justify-content: space-between; padding: 4px 10px; border-bottom: 1px solid #222; font-size: 11px; }
.prop-row:last-child { border-bottom: none; }
.prop-key { color: #888; }
.prop-val { color: #ddd; text-align: right; max-width: 60%; word-break: break-all; }
.tree-node { padding: 3px 6px; cursor: pointer; border-radius: 3px; user-select: none; margin-bottom: 2px; }
.tree-node:hover { background: #252525; }
.tree-node.selected { background: #00adb5; color: #000; font-weight: 600; }
</style>
""";
    }

    private static string GetHeaderAndLayout(string title)
    {
        return $"""
<header>
  <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
  <div class="toolbar">
    <button class="btn" id="btn-reset-cam">Reset View</button>
    <button class="btn" id="btn-toggle-sec">Sectioning</button>
    <button class="btn" id="btn-wireframe">Wireframe</button>
    <button class="btn" id="btn-fullscreen">Fullscreen</button>
  </div>
</header>
<div class="container">
  <div class="left-bar">
    <div class="tab-row">
      <button class="tab-btn active" data-tab="tab-tree">Hierarchy</button>
      <button class="tab-btn" data-tab="tab-types">Types</button>
    </div>
    <div class="panel-content" id="tab-tree"></div>
    <div class="panel-content" id="tab-types" style="display:none;"></div>
  </div>
  <div id="viewport-container">
    <div class="hud-stats" id="hud-stats">Elements: 0 | Triangles: 0</div>
    <canvas id="glcanvas"></canvas>
    <div class="section-panel" id="sec-controls" style="display:none;">
      <label><input type="checkbox" id="clip-x-en"> Cut X</label>
      <input type="range" id="clip-x-val" min="-100" max="100" value="0">
      <label><input type="checkbox" id="clip-y-en"> Cut Y</label>
      <input type="range" id="clip-y-val" min="-100" max="100" value="0">
      <label><input type="checkbox" id="clip-z-en"> Cut Z</label>
      <input type="range" id="clip-z-val" min="-100" max="100" value="0">
    </div>
  </div>
  <div class="sidebar">
    <div class="panel-header">Property Inspector</div>
    <div class="panel-content" id="prop-inspector">
      <div style="color: #666; font-size: 12px; text-align: center; margin-top: 40px;">Select an IFC element to inspect parameters</div>
    </div>
  </div>
</div>
""";
    }

    private static string GetViewerScript()
    {
        return """
<script>
(function() {
  const modelData = JSON.parse(document.getElementById('ifc-data').textContent);
  const canvas = document.getElementById('glcanvas');
  const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');

  if (!gl) {
    alert('WebGL is not supported by your browser.');
    return;
  }

  // Camera State
  let camTarget = [0, 0, 0];
  let camRadius = 10;
  let camTheta = 0.8;
  let camPhi = 0.5;
  let isDragging = false;
  let isPanning = false;
  let lastMouseX = 0;
  let lastMouseY = 0;
  let selectedElementIndex = -1;
  let wireframeMode = false;

  // Clipping Planes State
  const clipPlanes = {
    xEn: false, xVal: 0,
    yEn: false, yVal: 0,
    zEn: false, zVal: 0
  };

  // Compute Model Bounds & Center
  const bbox = modelData.boundingBox;
  if (bbox && bbox.min && bbox.max) {
    camTarget = [
      (bbox.min[0] + bbox.max[0]) * 0.5,
      (bbox.min[1] + bbox.max[1]) * 0.5,
      (bbox.min[2] + bbox.max[2]) * 0.5
    ];
    const dx = bbox.max[0] - bbox.min[0];
    const dy = bbox.max[1] - bbox.min[1];
    const dz = bbox.max[2] - bbox.min[2];
    camRadius = Math.max(Math.sqrt(dx*dx + dy*dy + dz*dz) * 1.3, 5.0);
  }

  const initialCam = { target: [...camTarget], radius: camRadius, theta: camTheta, phi: camPhi };

  // Update HUD
  const totalTriangles = modelData.elements.reduce((acc, e) => acc + (e.indices.length / 3), 0);
  document.getElementById('hud-stats').textContent = `Elements: ${modelData.elements.length} | Triangles: ${Math.round(totalTriangles).toLocaleString()}`;

  // Shaders
  const vsSource = `
    attribute vec3 aPosition;
    attribute vec3 aNormal;
    attribute vec4 aColor;
    uniform mat4 uProjection;
    uniform mat4 uView;
    uniform mat4 uModel;
    varying vec3 vNormal;
    varying vec3 vFragPos;
    varying vec4 vColor;
    void main() {
      vec4 worldPos = uModel * vec4(aPosition, 1.0);
      vFragPos = worldPos.xyz;
      vNormal = (uModel * vec4(aNormal, 0.0)).xyz;
      vColor = aColor;
      gl_Position = uProjection * uView * worldPos;
    }
  `;

  const fsSource = `
    precision mediump float;
    varying vec3 vNormal;
    varying vec3 vFragPos;
    varying vec4 vColor;
    uniform vec3 uLightDir;
    uniform bool uClipX;
    uniform float uClipXVal;
    uniform bool uClipY;
    uniform float uClipYVal;
    uniform bool uClipZ;
    uniform float uClipZVal;
    uniform bool uIsSelected;
    void main() {
      if (uClipX && vFragPos.x > uClipXVal) discard;
      if (uClipY && vFragPos.y > uClipYVal) discard;
      if (uClipZ && vFragPos.z > uClipZVal) discard;

      vec3 norm = normalize(vNormal);
      float diff = max(dot(norm, normalize(uLightDir)), 0.0);
      vec3 ambient = vec3(0.35, 0.35, 0.35);
      vec3 lightColor = vec3(0.8, 0.8, 0.8);
      vec3 base = vColor.rgb;
      if (uIsSelected) {
        base = vec3(0.0, 0.8, 1.0);
      }
      vec3 rgb = (ambient + diff * lightColor) * base;
      gl_FragColor = vec4(rgb, vColor.a);
    }
  `;

  function createShader(gl, type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      console.error(gl.getShaderInfoLog(shader));
      gl.deleteShader(shader);
      return null;
    }
    return shader;
  }

  const program = gl.createProgram();
  gl.attachShader(program, createShader(gl, gl.VERTEX_SHADER, vsSource));
  gl.attachShader(program, createShader(gl, gl.FRAGMENT_SHADER, fsSource));
  gl.linkProgram(program);

  const aPosition = gl.getAttribLocation(program, 'aPosition');
  const aNormal = gl.getAttribLocation(program, 'aNormal');
  const aColor = gl.getAttribLocation(program, 'aColor');
  const uProjection = gl.getUniformLocation(program, 'uProjection');
  const uView = gl.getUniformLocation(program, 'uView');
  const uModel = gl.getUniformLocation(program, 'uModel');
  const uLightDir = gl.getUniformLocation(program, 'uLightDir');
  const uClipX = gl.getUniformLocation(program, 'uClipX');
  const uClipXVal = gl.getUniformLocation(program, 'uClipXVal');
  const uClipY = gl.getUniformLocation(program, 'uClipY');
  const uClipYVal = gl.getUniformLocation(program, 'uClipYVal');
  const uClipZ = gl.getUniformLocation(program, 'uClipZ');
  const uClipZVal = gl.getUniformLocation(program, 'uClipZVal');
  const uIsSelected = gl.getUniformLocation(program, 'uIsSelected');

  // Build GPU buffers for each element mesh
  const glElements = [];
  modelData.elements.forEach((elem, index) => {
    if (!elem.vertices || elem.vertices.length === 0 || !elem.indices || elem.indices.length === 0) return;

    const verts = new Float32Array(elem.vertices);
    const indices = new Uint16Array(elem.indices);
    const normals = new Float32Array(verts.length);

    // Compute facet normals
    for (let i = 0; i < indices.length; i += 3) {
      const i0 = indices[i] * 3;
      const i1 = indices[i+1] * 3;
      const i2 = indices[i+2] * 3;
      const ax = verts[i1] - verts[i0];
      const ay = verts[i1+1] - verts[i0+1];
      const az = verts[i1+2] - verts[i0+2];
      const bx = verts[i2] - verts[i0];
      const by = verts[i2+1] - verts[i0+1];
      const bz = verts[i2+2] - verts[i0+2];
      let nx = ay * bz - az * by;
      let ny = az * bx - ax * bz;
      let nz = ax * by - ay * bx;
      const len = Math.sqrt(nx*nx + ny*ny + nz*nz);
      if (len > 1e-6) { nx /= len; ny /= len; nz /= len; }
      normals[i0] += nx; normals[i0+1] += ny; normals[i0+2] += nz;
      normals[i1] += nx; normals[i1+1] += ny; normals[i1+2] += nz;
      normals[i2] += nx; normals[i2+1] += ny; normals[i2+2] += nz;
    }

    const vbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
    gl.bufferData(gl.ARRAY_BUFFER, verts, gl.STATIC_DRAW);

    const nbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, nbo);
    gl.bufferData(gl.ARRAY_BUFFER, normals, gl.STATIC_DRAW);

    const ibo = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);

    glElements.push({
      index: index,
      vbo: vbo,
      nbo: nbo,
      ibo: ibo,
      count: indices.length,
      color: elem.color || [0.75, 0.75, 0.75, 1.0],
      visible: true
    });
  });

  // Math Helpers
  function mat4Perspective(out, fovy, aspect, near, far) {
    const f = 1.0 / Math.tan(fovy / 2);
    out.fill(0);
    out[0] = f / aspect;
    out[5] = f;
    out[10] = (far + near) / (near - far);
    out[11] = -1;
    out[14] = (2 * far * near) / (near - far);
    return out;
  }

  function mat4LookAt(out, eye, center, up) {
    let z0 = eye[0] - center[0], z1 = eye[1] - center[1], z2 = eye[2] - center[2];
    let len = Math.sqrt(z0*z0 + z1*z1 + z2*z2);
    if (len > 0) { z0 /= len; z1 /= len; z2 /= len; }
    let x0 = up[1]*z2 - up[2]*z1, x1 = up[2]*z0 - up[0]*z2, x2 = up[0]*z1 - up[1]*z0;
    len = Math.sqrt(x0*x0 + x1*x1 + x2*x2);
    if (len > 0) { x0 /= len; x1 /= len; x2 /= len; }
    let y0 = z1*x2 - z2*x1, y1 = z2*x0 - z0*x2, y2 = z0*x1 - z1*x0;
    out[0] = x0; out[1] = y0; out[2] = z0; out[3] = 0;
    out[4] = x1; out[5] = y1; out[6] = z1; out[7] = 0;
    out[8] = x2; out[9] = y2; out[10] = z2; out[11] = 0;
    out[12] = -(x0*eye[0] + x1*eye[1] + x2*eye[2]);
    out[13] = -(y0*eye[0] + y1*eye[1] + y2*eye[2]);
    out[14] = -(z0*eye[0] + z1*eye[1] + z2*eye[2]);
    out[15] = 1;
    return out;
  }

  const projMatrix = new Float32Array(16);
  const viewMatrix = new Float32Array(16);
  const modelMatrix = new Float32Array([1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]);

  function render() {
    canvas.width = canvas.clientWidth * window.devicePixelRatio;
    canvas.height = canvas.clientHeight * window.devicePixelRatio;
    gl.viewport(0, 0, canvas.width, canvas.height);

    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    gl.clearColor(0.12, 0.13, 0.15, 1.0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    const aspect = canvas.width / canvas.height;
    mat4Perspective(projMatrix, 45 * Math.PI / 180, aspect, 0.1, 2000.0);

    const eyeX = camTarget[0] + camRadius * Math.sin(camPhi) * Math.sin(camTheta);
    const eyeY = camTarget[1] + camRadius * Math.sin(camPhi) * Math.cos(camTheta);
    const eyeZ = camTarget[2] + camRadius * Math.cos(camPhi);
    mat4LookAt(viewMatrix, [eyeX, eyeY, eyeZ], camTarget, [0, 0, 1]);

    gl.useProgram(program);
    gl.uniformMatrix4fv(uProjection, false, projMatrix);
    gl.uniformMatrix4fv(uView, false, viewMatrix);
    gl.uniformMatrix4fv(uModel, false, modelMatrix);
    gl.uniform3f(uLightDir, 0.6, 0.4, 0.8);

    gl.uniform1i(uClipX, clipPlanes.xEn ? 1 : 0);
    gl.uniform1f(uClipXVal, clipPlanes.xVal);
    gl.uniform1i(uClipY, clipPlanes.yEn ? 1 : 0);
    gl.uniform1f(uClipYVal, clipPlanes.yVal);
    gl.uniform1i(uClipZ, clipPlanes.zEn ? 1 : 0);
    gl.uniform1f(uClipZVal, clipPlanes.zVal);

    for (let i = 0; i < glElements.length; i++) {
      const g = glElements[i];
      if (!g.visible) continue;

      gl.uniform1i(uIsSelected, g.index === selectedElementIndex ? 1 : 0);
      gl.vertexAttrib4fv(aColor, g.color);

      gl.bindBuffer(gl.ARRAY_BUFFER, g.vbo);
      gl.enableVertexAttribArray(aPosition);
      gl.vertexAttribPointer(aPosition, 3, gl.FLOAT, false, 0, 0);

      gl.bindBuffer(gl.ARRAY_BUFFER, g.nbo);
      gl.enableVertexAttribArray(aNormal);
      gl.vertexAttribPointer(aNormal, 3, gl.FLOAT, false, 0, 0);

      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, g.ibo);
      gl.drawElements(wireframeMode ? gl.LINES : gl.TRIANGLES, g.count, gl.UNSIGNED_SHORT, 0);
    }
  }

  // Mouse / Orbit controls
  canvas.addEventListener('mousedown', e => {
    isDragging = (e.button === 0);
    isPanning = (e.button === 2);
    lastMouseX = e.clientX;
    lastMouseY = e.clientY;
  });

  window.addEventListener('mouseup', () => { isDragging = false; isPanning = false; });
  canvas.addEventListener('contextmenu', e => e.preventDefault());

  canvas.addEventListener('mousemove', e => {
    const dx = e.clientX - lastMouseX;
    const dy = e.clientY - lastMouseY;
    lastMouseX = e.clientX;
    lastMouseY = e.clientY;

    if (isDragging) {
      camTheta -= dx * 0.007;
      camPhi -= dy * 0.007;
      camPhi = Math.max(0.01, Math.min(Math.PI - 0.01, camPhi));
      render();
    } else if (isPanning) {
      const panSpeed = camRadius * 0.0015;
      camTarget[0] -= dx * panSpeed * Math.cos(camTheta);
      camTarget[1] += dx * panSpeed * Math.sin(camTheta);
      camTarget[2] += dy * panSpeed;
      render();
    }
  });

  canvas.addEventListener('wheel', e => {
    e.preventDefault();
    camRadius *= (e.deltaY > 0 ? 1.1 : 0.9);
    camRadius = Math.max(1.0, Math.min(2000.0, camRadius));
    render();
  }, { passive: false });

  // Buttons & Toolbar
  document.getElementById('btn-reset-cam').onclick = () => {
    camTarget = [...initialCam.target];
    camRadius = initialCam.radius;
    camTheta = initialCam.theta;
    camPhi = initialCam.phi;
    render();
  };

  document.getElementById('btn-wireframe').onclick = () => {
    wireframeMode = !wireframeMode;
    render();
  };

  document.getElementById('btn-fullscreen').onclick = () => {
    if (!document.fullscreenElement) document.documentElement.requestFullscreen();
    else document.exitFullscreen();
  };

  const secControls = document.getElementById('sec-controls');
  document.getElementById('btn-toggle-sec').onclick = () => {
    secControls.style.display = secControls.style.display === 'none' ? 'block' : 'none';
  };

  // Section Sliders
  if (bbox && bbox.min && bbox.max) {
    document.getElementById('clip-x-val').min = bbox.min[0];
    document.getElementById('clip-x-val').max = bbox.max[0];
    document.getElementById('clip-x-val').value = (bbox.min[0] + bbox.max[0]) * 0.5;

    document.getElementById('clip-y-val').min = bbox.min[1];
    document.getElementById('clip-y-val').max = bbox.max[1];
    document.getElementById('clip-y-val').value = (bbox.min[1] + bbox.max[1]) * 0.5;

    document.getElementById('clip-z-val').min = bbox.min[2];
    document.getElementById('clip-z-val').max = bbox.max[2];
    document.getElementById('clip-z-val').value = (bbox.min[2] + bbox.max[2]) * 0.5;
  }

  document.getElementById('clip-x-en').onchange = e => { clipPlanes.xEn = e.target.checked; render(); };
  document.getElementById('clip-x-val').oninput = e => { clipPlanes.xVal = parseFloat(e.target.value); render(); };
  document.getElementById('clip-y-en').onchange = e => { clipPlanes.yEn = e.target.checked; render(); };
  document.getElementById('clip-y-val').oninput = e => { clipPlanes.yVal = parseFloat(e.target.value); render(); };
  document.getElementById('clip-z-en').onchange = e => { clipPlanes.zEn = e.target.checked; render(); };
  document.getElementById('clip-z-val').oninput = e => { clipPlanes.zVal = parseFloat(e.target.value); render(); };

  // Element Selection & Property Inspector
  function showProperties(index) {
    selectedElementIndex = index;
    render();

    const inspector = document.getElementById('prop-inspector');
    if (index < 0 || index >= modelData.elements.length) {
      inspector.innerHTML = '<div style="color: #666; font-size: 12px; text-align: center; margin-top: 40px;">Select an IFC element to inspect parameters</div>';
      return;
    }

    const elem = modelData.elements[index];
    let html = '';

    // Identity Card
    html += '<div class="prop-card">';
    html += '<div class="prop-card-title">Element Identity</div>';
    html += `<div class="prop-row"><span class="prop-key">Name</span><span class="prop-val">${elem.name || 'Unnamed'}</span></div>`;
    html += `<div class="prop-row"><span class="prop-key">Type</span><span class="prop-val">${elem.ifcType}</span></div>`;
    html += `<div class="prop-row"><span class="prop-key">Storey</span><span class="prop-val">${elem.storey || 'None'}</span></div>`;
    if (elem.globalId) {
      html += `<div class="prop-row"><span class="prop-key">GlobalId</span><span class="prop-val">${elem.globalId}</span></div>`;
    }
    if (elem.material) {
      html += `<div class="prop-row"><span class="prop-key">Material</span><span class="prop-val">${elem.material}</span></div>`;
    }
    html += '</div>';

    // PropertySets
    if (elem.propertySets) {
      for (const [pset, props] of Object.entries(elem.propertySets)) {
        html += `<div class="prop-card"><div class="prop-card-title">${pset}</div>`;
        for (const [k, v] of Object.entries(props)) {
          html += `<div class="prop-row"><span class="prop-key">${k}</span><span class="prop-val">${v}</span></div>`;
        }
        html += '</div>';
      }
    }

    // Quantities
    if (elem.quantities) {
      for (const [qset, qs] of Object.entries(elem.quantities)) {
        html += `<div class="prop-card"><div class="prop-card-title">${qset}</div>`;
        for (const [k, v] of Object.entries(qs)) {
          html += `<div class="prop-row"><span class="prop-key">${k}</span><span class="prop-val">${v}</span></div>`;
        }
        html += '</div>';
      }
    }

    inspector.innerHTML = html;
  }

  // Populate Spatial Tree
  const treeContainer = document.getElementById('tab-tree');
  const storeys = {};
  modelData.elements.forEach((elem, idx) => {
    const sName = elem.storey || 'Unassigned';
    if (!storeys[sName]) storeys[sName] = [];
    storeys[sName].push({ elem, idx });
  });

  let treeHtml = '';
  for (const [storey, items] of Object.entries(storeys)) {
    treeHtml += `<div style="font-weight: 600; color: #00adb5; margin-top: 8px; margin-bottom: 4px;">Storey: ${storey} (${items.length})</div>`;
    items.forEach(item => {
      treeHtml += `<div class="tree-node" data-index="${item.idx}">${item.elem.ifcType}: ${item.elem.name}</div>`;
    });
  }
  treeContainer.innerHTML = treeHtml;

  treeContainer.addEventListener('click', e => {
    const target = e.target.closest('.tree-node');
    if (!target) return;
    document.querySelectorAll('.tree-node').forEach(n => n.classList.remove('selected'));
    target.classList.add('selected');
    const idx = parseInt(target.getAttribute('data-index'));
    showProperties(idx);
  });

  // Populate Types tab
  const typesContainer = document.getElementById('tab-types');
  const types = {};
  modelData.elements.forEach(elem => {
    types[elem.ifcType] = (types[elem.ifcType] || 0) + 1;
  });
  let typesHtml = '';
  for (const [t, count] of Object.entries(types)) {
    typesHtml += `<div class="prop-row"><span class="prop-key">${t}</span><span class="prop-val">${count}</span></div>`;
  }
  typesContainer.innerHTML = typesHtml;

  // Tabs Switcher
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.onclick = () => {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      const tabId = btn.getAttribute('data-tab');
      document.getElementById('tab-tree').style.display = tabId === 'tab-tree' ? 'block' : 'none';
      document.getElementById('tab-types').style.display = tabId === 'tab-types' ? 'block' : 'none';
    };
  });

  window.addEventListener('resize', render);
  render();
})();
</script>
""";
    }

    private static string BuildJsonPayload(IfcModel model, IfcHtmlViewerOptions options)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("title", options.Title);

        // Compute overall bounding box
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        var storeyMap = new Dictionary<int, string>();
        foreach (var s in model.Storeys)
        {
            foreach (var e in s.Elements)
            {
                storeyMap[e.Id] = s.Name;
            }
        }

        var elementsToExport = new List<(IfcElement Element, IfcMesh Mesh)>();
        foreach (var elem in model.AllElements)
        {
            var mesh = elem.GetEffectiveMesh();
            if (mesh is not null && mesh.Vertices.Count >= 3 && mesh.Indices.Count >= 3)
            {
                elementsToExport.Add((elem, mesh));
                var box = mesh.BoundingBox;
                if (!box.IsEmpty)
                {
                    if (box.Min.X < minX) minX = box.Min.X;
                    if (box.Min.Y < minY) minY = box.Min.Y;
                    if (box.Min.Z < minZ) minZ = box.Min.Z;
                    if (box.Max.X > maxX) maxX = box.Max.X;
                    if (box.Max.Y > maxY) maxY = box.Max.Y;
                    if (box.Max.Z > maxZ) maxZ = box.Max.Z;
                }
            }
        }

        if (double.IsInfinity(minX))
        {
            minX = -10; minY = -10; minZ = 0;
            maxX = 10; maxY = 10; maxZ = 5;
        }

        writer.WriteStartObject("boundingBox");
        writer.WriteStartArray("min");
        writer.WriteNumberValue(minX);
        writer.WriteNumberValue(minY);
        writer.WriteNumberValue(minZ);
        writer.WriteEndArray();
        writer.WriteStartArray("max");
        writer.WriteNumberValue(maxX);
        writer.WriteNumberValue(maxY);
        writer.WriteNumberValue(maxZ);
        writer.WriteEndArray();
        writer.WriteEndObject();

        // Elements
        writer.WriteStartArray("elements");
        int totalVerts = 0;
        foreach (var (elem, mesh) in elementsToExport)
        {
            if (totalVerts >= options.MaxMeshVertices) break;

            writer.WriteStartObject();
            writer.WriteNumber("id", elem.Id);
            writer.WriteString("name", elem.Name);
            writer.WriteString("ifcType", elem.IfcType);
            if (!string.IsNullOrEmpty(elem.GlobalId)) writer.WriteString("globalId", elem.GlobalId);
            if (storeyMap.TryGetValue(elem.Id, out var sName)) writer.WriteString("storey", sName);
            if (elem.Material is not null) writer.WriteString("material", elem.Material.Name);

            // RGBA color
            var col = GetElementColor(elem);
            writer.WriteStartArray("color");
            writer.WriteNumberValue(col.r);
            writer.WriteNumberValue(col.g);
            writer.WriteNumberValue(col.b);
            writer.WriteNumberValue(col.a);
            writer.WriteEndArray();

            // Vertices
            writer.WriteStartArray("vertices");
            foreach (var v in mesh.Vertices)
            {
                writer.WriteNumberValue(v.X);
                writer.WriteNumberValue(v.Y);
                writer.WriteNumberValue(v.Z);
            }
            writer.WriteEndArray();

            // Indices
            writer.WriteStartArray("indices");
            foreach (var idx in mesh.Indices)
            {
                writer.WriteNumberValue(idx);
            }
            writer.WriteEndArray();

            // Properties
            if (options.IncludeProperties && elem.PropertySets.Count > 0)
            {
                writer.WriteStartObject("propertySets");
                foreach (var pset in elem.PropertySets)
                {
                    writer.WriteStartObject(pset.Name);
                    foreach (var prop in pset.Properties)
                    {
                        if (!string.IsNullOrEmpty(prop.Name))
                        {
                            writer.WriteString(prop.Name, prop.Value);
                        }
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }

            // Quantities
            if (options.IncludeQuantities && elem.QuantitySets.Count > 0)
            {
                writer.WriteStartObject("quantities");
                foreach (var qset in elem.QuantitySets)
                {
                    writer.WriteStartObject(qset.Name);
                    foreach (var q in qset.Quantities)
                    {
                        if (!string.IsNullOrEmpty(q.Name))
                        {
                            writer.WriteNumber(q.Name, q.Value);
                        }
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            totalVerts += mesh.Vertices.Count;
        }
        writer.WriteEndArray(); // elements

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static (double r, double g, double b, double a) GetElementColor(IfcElement elem)
    {
        if (elem.Material is not null)
        {
            return (elem.Material.R, elem.Material.G, elem.Material.B, elem.Material.Alpha);
        }

        string type = elem.IfcType.ToUpperInvariant();
        if (type.Contains("WALL", StringComparison.Ordinal)) return (0.85, 0.85, 0.80, 1.0);
        if (type.Contains("SLAB", StringComparison.Ordinal) || type.Contains("FOOTING", StringComparison.Ordinal)) return (0.75, 0.75, 0.75, 1.0);
        if (type.Contains("COLUMN", StringComparison.Ordinal) || type.Contains("BEAM", StringComparison.Ordinal)) return (0.45, 0.55, 0.75, 1.0);
        if (type.Contains("WINDOW", StringComparison.Ordinal) || type.Contains("CURTAINWALL", StringComparison.Ordinal)) return (0.4, 0.7, 0.9, 0.5);
        if (type.Contains("DOOR", StringComparison.Ordinal)) return (0.6, 0.4, 0.2, 1.0);
        if (type.Contains("FLOW", StringComparison.Ordinal) || type.Contains("PIPE", StringComparison.Ordinal)) return (0.9, 0.4, 0.2, 1.0);
        if (type.Contains("DUCT", StringComparison.Ordinal)) return (0.2, 0.8, 0.4, 1.0);

        return (0.7, 0.7, 0.7, 1.0);
    }
}
