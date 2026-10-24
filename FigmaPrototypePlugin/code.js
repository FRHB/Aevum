// Figma Plugin: Aevum Blazor UI Recreator
// Reverse-engineered from PhotoAevumStudio Blazor codebase
// Compatible with Figma Plugin API, Figma Console, and Scripter
// In-place Update Support: Re-running updates existing frames without duplication!

(async function main() {
  figma.notify("Starting Aevum UI generation...", { timeout: 2000 });

  // 1. Preload required font weights
  const fonts = [
    { family: "Inter", style: "Regular" },
    { family: "Inter", style: "Medium" },
    { family: "Inter", style: "Semi Bold" },
    { family: "Inter", style: "Bold" }
  ];

  for (const f of fonts) {
    try {
      await figma.loadFontAsync(f);
    } catch (e) {
      console.warn("Could not load font " + f.family + " " + f.style, e);
    }
  }

  // 2. Helper functions
  function hexToRgb(hex) {
    hex = hex.replace("#", "");
    if (hex.length === 3) {
      hex = hex.split("").map(c => c + c).join("");
    }
    const num = parseInt(hex, 16);
    return {
      r: ((num >> 16) & 255) / 255,
      g: ((num >> 8) & 255) / 255,
      b: (num & 255) / 255
    };
  }

  function solidFill(hex, opacity = 1) {
    return [{ type: "SOLID", color: hexToRgb(hex), opacity }];
  }

  function createText(text, size = 14, style = "Regular", colorHex = "#1a1a1a") {
    const t = figma.createText();
    t.fontName = { family: "Inter", style: style };
    t.fontSize = size;
    t.characters = text || "";
    t.fills = solidFill(colorHex);
    return t;
  }

  function applyAutoLayout(frame, mode = "VERTICAL", spacing = 16, padding = [0, 0, 0, 0]) {
    frame.layoutMode = mode;
    frame.itemSpacing = spacing;
    frame.paddingTop = padding[0];
    frame.paddingRight = padding[1];
    frame.paddingBottom = padding[2];
    frame.paddingLeft = padding[3];
  }

  function addDropShadow(node, y = 2, blur = 6, opacity = 0.08) {
    node.effects = [{
      type: "DROP_SHADOW",
      color: { r: 0, g: 0, b: 0, a: opacity },
      offset: { x: 0, y },
      radius: blur,
      spread: 0,
      visible: true,
      blendMode: "NORMAL"
    }];
  }

  // Tracking counters for in-place updates vs new creations
  let updatedCount = 0;
  let createdCount = 0;

  function getOrCreateTopFrame(name, width, height, defaultX, defaultY) {
    const existing = figma.currentPage.findOne(n => n.name === name && n.type === "FRAME");
    if (existing) {
      // Clear previous children to rebuild fresh in-place
      while (existing.children.length > 0) {
        existing.children[0].remove();
      }
      existing.resize(width, height);
      updatedCount++;
      return existing;
    }
    const frame = figma.createFrame();
    frame.name = name;
    frame.x = defaultX;
    frame.y = defaultY;
    frame.resize(width, height);
    createdCount++;
    return frame;
  }

  // Color Constants (Extracted from tailwind.config and app.css)
  const C = {
    brand50: "#f6f6f6",
    brand100: "#e7e7e7",
    brand500: "#1a1a1a",
    brand900: "#000000",
    ink: "#1a1a1a",
    muted: "#6b7280",
    line: "#e5e7eb",
    danger: "#b42318",
    dangerBg: "#fee2e2",
    teal: "#0f766e",
    tealBg: "#ccfbf1",
    blue: "#2563eb",
    blueBg: "#eff6ff",
    amber: "#d97706",
    amberBg: "#fef9c3",
    emerald: "#166534",
    emeraldBg: "#dcfce7",
    violet: "#7c3aed",
    violetBg: "#f5f3ff",
    shellBg: "#f8f9fa",
    white: "#ffffff",
    cardBorder: "#e2e8f0",
    slate50: "#f8fafc",
    slate100: "#f1f5f9",
    slate400: "#94a3b8",
    slate500: "#64748b",
    slate700: "#334155",
    slate900: "#0f172a"
  };

  // Tracking X offset for generated canvas frames
  let canvasX = 0;
  const GAP_X = 100;
  const allCreatedFrames = [];

  // =========================================================================
  // FRAME 1: DESIGN TOKENS & STYLE GUIDE
  // =========================================================================
  const tokensFrame = getOrCreateTopFrame("1. Global Styles & Design Tokens", 1100, 1050, canvasX, 0);
  tokensFrame.fills = solidFill(C.white);
  tokensFrame.cornerRadius = 16;
  applyAutoLayout(tokensFrame, "VERTICAL", 32, [40, 40, 40, 40]);
  addDropShadow(tokensFrame, 4, 20, 0.06);

  const tokensHeader = figma.createFrame();
  tokensHeader.fills = [];
  applyAutoLayout(tokensHeader, "VERTICAL", 8, [0, 0, 0, 0]);
  tokensHeader.appendChild(createText("Photo Aevum Studio - Design System", 28, "Bold", C.slate900));
  tokensHeader.appendChild(createText("Reverse-engineered tokens from tailwind.config.js & /wwwroot/app.css", 14, "Regular", C.muted));
  tokensFrame.appendChild(tokensHeader);

  // Palette Section
  const paletteSection = figma.createFrame();
  paletteSection.fills = [];
  applyAutoLayout(paletteSection, "VERTICAL", 16, [0, 0, 0, 0]);
  paletteSection.appendChild(createText("Color Palette & Semantic Tokens", 18, "Semi Bold", C.slate900));

  const swatchesRow1 = figma.createFrame();
  swatchesRow1.fills = [];
  applyAutoLayout(swatchesRow1, "HORIZONTAL", 16, [0, 0, 0, 0]);

  function makeSwatch(name, hex, label) {
    const sw = figma.createFrame();
    sw.resize(180, 110);
    sw.fills = solidFill(C.white);
    sw.cornerRadius = 8;
    sw.strokes = solidFill(C.line);
    sw.strokeWeight = 1;
    applyAutoLayout(sw, "VERTICAL", 6, [8, 8, 8, 8]);

    const colorBox = figma.createFrame();
    colorBox.resize(164, 48);
    colorBox.fills = solidFill(hex);
    colorBox.cornerRadius = 4;
    sw.appendChild(colorBox);

    sw.appendChild(createText(name, 12, "Bold", C.slate900));
    sw.appendChild(createText(hex + " - " + label, 11, "Regular", C.muted));
    return sw;
  }

  swatchesRow1.appendChild(makeSwatch("Brand 900 / Primary", C.brand900, "Dark CTA"));
  swatchesRow1.appendChild(makeSwatch("Ink / Text", C.ink, "Body / Headers"));
  swatchesRow1.appendChild(makeSwatch("Accent Teal", C.teal, "Active / Brand"));
  swatchesRow1.appendChild(makeSwatch("Line / Border", C.line, "Dividers"));
  swatchesRow1.appendChild(makeSwatch("Muted / Text", C.muted, "Subtitles"));
  paletteSection.appendChild(swatchesRow1);

  const swatchesRow2 = figma.createFrame();
  swatchesRow2.fills = [];
  applyAutoLayout(swatchesRow2, "HORIZONTAL", 16, [0, 0, 0, 0]);
  swatchesRow2.appendChild(makeSwatch("Success / Emerald", C.emerald, "Approved"));
  swatchesRow2.appendChild(makeSwatch("Warning / Amber", C.amber, "Pending"));
  swatchesRow2.appendChild(makeSwatch("Danger / Crimson", C.danger, "Alerts"));
  swatchesRow2.appendChild(makeSwatch("Info / Blue", C.blue, "Highlights"));
  swatchesRow2.appendChild(makeSwatch("Violet Accent", C.violet, "Special"));
  paletteSection.appendChild(swatchesRow2);

  tokensFrame.appendChild(paletteSection);

  // Typography Section
  const typoSection = figma.createFrame();
  typoSection.fills = [];
  applyAutoLayout(typoSection, "VERTICAL", 16, [0, 0, 0, 0]);
  typoSection.appendChild(createText("Typography Hierarchy (Inter)", 18, "Semi Bold", C.slate900));

  const typoRow = figma.createFrame();
  typoRow.fills = solidFill(C.slate50);
  typoRow.cornerRadius = 12;
  applyAutoLayout(typoRow, "VERTICAL", 14, [20, 24, 20, 24]);
  typoRow.appendChild(createText("Display / Hero: Inter Bold 32px", 32, "Bold", C.slate900));
  typoRow.appendChild(createText("Page Header: Inter Bold 24px (Dashboard Titles)", 24, "Bold", C.slate900));
  typoRow.appendChild(createText("Card Heading: Inter Semi-Bold 18px", 18, "Semi Bold", C.slate900));
  typoRow.appendChild(createText("Body Text: Inter Regular 14px - Standard UI copy and data table cells", 14, "Regular", C.slate700));
  typoRow.appendChild(createText("Small / Meta: Inter Medium 12px (Labels & Toolbars)", 12, "Medium", C.muted));
  typoRow.appendChild(createText("MICRO / BADGE: INTER BOLD 10PX UPPERCASE TRACKING-WIDER", 10, "Bold", C.muted));
  typoSection.appendChild(typoRow);
  tokensFrame.appendChild(typoSection);

  allCreatedFrames.push(tokensFrame);
  canvasX += 1100 + GAP_X;

  // =========================================================================
  // HELPER: BUILD SIDEBAR COMPONENT
  // =========================================================================
  function buildSidebar(role = "admin", activePath = "/admin") {
    const sb = figma.createFrame();
    sb.name = "Sidebar (" + role + ")";
    sb.resize(240, 900);
    sb.fills = solidFill(C.white);
    sb.strokes = solidFill(C.line);
    sb.strokeWeight = 1;
    applyAutoLayout(sb, "VERTICAL", 20, [24, 16, 24, 16]);

    const brand = figma.createFrame();
    brand.fills = [];
    applyAutoLayout(brand, "HORIZONTAL", 12, [0, 0, 0, 0]);
    brand.counterAxisAlignItems = "CENTER";

    const logo = figma.createEllipse();
    logo.resize(36, 36);
    logo.fills = solidFill(C.ink);
    brand.appendChild(logo);

    const brandCol = figma.createFrame();
    brandCol.fills = [];
    applyAutoLayout(brandCol, "VERTICAL", 2, [0, 0, 0, 0]);
    brandCol.appendChild(createText("Aevum Studio", 15, "Bold", C.slate900));
    brandCol.appendChild(createText("PHOTO & VIDEO", 9, "Bold", C.muted));
    brand.appendChild(brandCol);
    sb.appendChild(brand);

    function makeNavItem(label, iconText, isActive = false) {
      const item = figma.createFrame();
      item.resize(208, 38);
      item.cornerRadius = 8;
      item.fills = isActive ? solidFill(C.slate100) : [];
      applyAutoLayout(item, "HORIZONTAL", 10, [8, 12, 8, 12]);
      item.counterAxisAlignItems = "CENTER";

      item.appendChild(createText(iconText, 14, "Regular", isActive ? C.teal : C.muted));
      item.appendChild(createText(label, 13, isActive ? "Semi Bold" : "Medium", isActive ? C.slate900 : C.slate700));
      return item;
    }

    function makeSectionLabel(title) {
      return createText(title.toUpperCase(), 10, "Bold", C.muted);
    }

    if (role === "admin") {
      sb.appendChild(makeSectionLabel("Overview"));
      sb.appendChild(makeNavItem("Dashboard", "📊", activePath === "/admin"));

      sb.appendChild(makeSectionLabel("Booking Management"));
      sb.appendChild(makeNavItem("Approvals", "✓", activePath === "/admin/approvals"));
      sb.appendChild(makeNavItem("Calendar of Events", "📅", activePath === "/admin/calendar/events"));
      sb.appendChild(makeNavItem("Staff Schedules", "⏱", activePath === "/admin/calendar/staff"));

      sb.appendChild(makeSectionLabel("Catalog & Resources"));
      sb.appendChild(makeNavItem("Services & Packages", "🏷", activePath === "/admin/services"));
      sb.appendChild(makeNavItem("Staff Directory", "👥", activePath === "/admin/employees"));
      sb.appendChild(makeNavItem("Equipment Inventory", "📷", activePath === "/admin/equipments"));

      sb.appendChild(makeSectionLabel("Users & System"));
      sb.appendChild(makeNavItem("Clients", "📁", activePath === "/admin/clients"));
      sb.appendChild(makeNavItem("Admin Settings", "⚙", activePath === "/admin/profile"));
    } else if (role === "employee") {
      sb.appendChild(makeSectionLabel("Operations"));
      sb.appendChild(makeNavItem("Staff Dashboard", "📊", activePath === "/staff"));
      sb.appendChild(makeNavItem("My Assignments", "📋", activePath === "/staff/assignments"));
      sb.appendChild(makeNavItem("Master Calendar", "📅", activePath === "/staff/calendar"));
      sb.appendChild(makeNavItem("Delivery Pipeline", "🔄", activePath === "/staff/pipeline"));
      sb.appendChild(makeNavItem("Gear Checklist", "📷", activePath === "/staff/gear"));
      sb.appendChild(makeSectionLabel("Account"));
      sb.appendChild(makeNavItem("My Profile", "👤", activePath === "/staff/profile"));
    } else {
      sb.appendChild(makeSectionLabel("Client Hub"));
      sb.appendChild(makeNavItem("Portal Overview", "🏠", activePath === "/my"));
      sb.appendChild(makeNavItem("My Appointments", "📋", activePath === "/my/bookings"));
      sb.appendChild(makeNavItem("Book a Session", "➕", activePath === "/my/book"));
      sb.appendChild(makeNavItem("Private Galleries", "🖼", activePath === "/my/galleries"));
      sb.appendChild(makeSectionLabel("Account"));
      sb.appendChild(makeNavItem("Profile & Security", "👤", activePath === "/my/profile"));
    }

    const spacer = figma.createFrame();
    spacer.fills = [];
    spacer.resize(208, 40);
    sb.appendChild(spacer);

    const logoutItem = figma.createFrame();
    logoutItem.resize(208, 36);
    logoutItem.fills = [];
    applyAutoLayout(logoutItem, "HORIZONTAL", 10, [6, 12, 6, 12]);
    logoutItem.appendChild(createText("🚪", 14, "Regular", C.danger));
    logoutItem.appendChild(createText("Log Out", 13, "Semi Bold", C.danger));
    sb.appendChild(logoutItem);

    return sb;
  }

  // =========================================================================
  // HELPER: BUILD KPI STAT CARD
  // =========================================================================
  function buildKpiCard(title, value, icon, colorKey = "teal", width = 250) {
    const card = figma.createFrame();
    card.name = "Stat Card - " + title;
    card.resize(width, 105);
    card.fills = solidFill(C.white);
    card.cornerRadius = 12;
    card.strokes = solidFill(C.cardBorder);
    card.strokeWeight = 1;
    applyAutoLayout(card, "HORIZONTAL", 16, [18, 20, 18, 20]);
    card.counterAxisAlignItems = "CENTER";
    addDropShadow(card, 2, 4, 0.04);

    const iconBox = figma.createFrame();
    iconBox.resize(48, 48);
    iconBox.cornerRadius = 10;
    
    let bg = C.tealBg;
    let iconColor = C.teal;
    if (colorKey === "blue") { bg = C.blueBg; iconColor = C.blue; }
    else if (colorKey === "amber") { bg = C.amberBg; iconColor = C.amber; }
    else if (colorKey === "violet") { bg = C.violetBg; iconColor = C.violet; }
    else if (colorKey === "emerald") { bg = C.emeraldBg; iconColor = C.emerald; }

    iconBox.fills = solidFill(bg);
    applyAutoLayout(iconBox, "VERTICAL", 0, [0, 0, 0, 0]);
    iconBox.primaryAxisAlignItems = "CENTER";
    iconBox.counterAxisAlignItems = "CENTER";
    iconBox.appendChild(createText(icon, 20, "Regular", iconColor));
    card.appendChild(iconBox);

    const textCol = figma.createFrame();
    textCol.fills = [];
    applyAutoLayout(textCol, "VERTICAL", 4, [0, 0, 0, 0]);
    textCol.appendChild(createText(title, 12, "Medium", C.slate500));
    textCol.appendChild(createText(value, 24, "Bold", C.slate900));
    card.appendChild(textCol);

    return card;
  }

  // =========================================================================
  // HELPER: STATUS BADGE
  // =========================================================================
  function makeBadge(text, type = "success") {
    const badge = figma.createFrame();
    badge.cornerRadius = 999;
    let bg = C.emeraldBg;
    let textCol = C.emerald;
    if (type === "warning") { bg = C.amberBg; textCol = C.amber; }
    else if (type === "danger") { bg = C.dangerBg; textCol = C.danger; }
    else if (type === "info") { bg = C.blueBg; textCol = C.blue; }

    badge.fills = solidFill(bg);
    applyAutoLayout(badge, "HORIZONTAL", 4, [4, 10, 4, 10]);
    badge.counterAxisAlignItems = "CENTER";
    badge.appendChild(createText(text, 11, "Semi Bold", textCol));
    return badge;
  }

  function makeButton(label, variant = "primary") {
    const btn = figma.createFrame();
    btn.cornerRadius = 8;
    applyAutoLayout(btn, "HORIZONTAL", 8, [10, 20, 10, 20]);
    btn.counterAxisAlignItems = "CENTER";

    if (variant === "primary") {
      btn.fills = solidFill(C.brand900);
      btn.appendChild(createText(label.toUpperCase(), 12, "Bold", C.white));
    } else if (variant === "ghost") {
      btn.fills = solidFill(C.white);
      btn.strokes = solidFill(C.line);
      btn.strokeWeight = 1;
      btn.appendChild(createText(label, 13, "Semi Bold", C.slate900));
    } else if (variant === "danger") {
      btn.fills = solidFill(C.dangerBg);
      btn.appendChild(createText(label, 13, "Bold", C.danger));
    }
    return btn;
  }

  function makeInput(label, placeholder, isPassword = false, width = 280) {
    const field = figma.createFrame();
    field.fills = [];
    applyAutoLayout(field, "VERTICAL", 6, [0, 0, 0, 0]);
    field.appendChild(createText(label, 12, "Semi Bold", C.slate700));

    const box = figma.createFrame();
    box.resize(width, 42);
    box.cornerRadius = 8;
    box.fills = solidFill(C.white);
    box.strokes = solidFill(C.line);
    box.strokeWeight = 1;
    applyAutoLayout(box, "HORIZONTAL", 10, [10, 14, 10, 14]);
    box.counterAxisAlignItems = "CENTER";
    box.appendChild(createText(placeholder, 13, "Regular", C.muted));

    if (isPassword) {
      const spacer = figma.createFrame();
      spacer.fills = [];
      spacer.resize(width - 140, 10);
      box.appendChild(spacer);
      box.appendChild(createText("👁", 14, "Regular", C.muted));
    }
    field.appendChild(box);
    return field;
  }

  function makeActionCard(title, desc, icon, w = 360) {
    const ac = figma.createFrame();
    ac.resize(w, 130);
    ac.fills = solidFill(C.white);
    ac.cornerRadius = 14;
    ac.strokes = solidFill(C.cardBorder);
    ac.strokeWeight = 1;
    applyAutoLayout(ac, "VERTICAL", 8, [20, 20, 20, 20]);
    addDropShadow(ac, 2, 6, 0.04);

    const row = figma.createFrame();
    row.fills = [];
    applyAutoLayout(row, "HORIZONTAL", 10, [0, 0, 0, 0]);
    row.appendChild(createText(icon, 18, "Regular", C.teal));
    row.appendChild(createText(title, 16, "Bold", C.slate900));
    ac.appendChild(row);

    ac.appendChild(createText(desc, 13, "Regular", C.muted));
    return ac;
  }

  // =========================================================================
  // FRAME 2: COMPONENT LIBRARY
  // =========================================================================
  const componentsFrame = getOrCreateTopFrame("2. Shared UI Components Library", 1100, 1050, canvasX, 0);
  componentsFrame.fills = solidFill(C.white);
  componentsFrame.cornerRadius = 16;
  applyAutoLayout(componentsFrame, "VERTICAL", 28, [40, 40, 40, 40]);
  addDropShadow(componentsFrame, 4, 20, 0.06);

  componentsFrame.appendChild(createText("Reusable UI Components", 24, "Bold", C.slate900));

  // Stat Cards Showcase
  componentsFrame.appendChild(createText("KPI Stat Cards (aev-stat-card)", 16, "Semi Bold", C.slate900));
  const statsRow = figma.createFrame();
  statsRow.fills = [];
  applyAutoLayout(statsRow, "HORIZONTAL", 16, [0, 0, 0, 0]);
  statsRow.appendChild(buildKpiCard("Appointments Today", "12", "📅", "teal", 230));
  statsRow.appendChild(buildKpiCard("Active Personnel", "8", "👥", "blue", 230));
  statsRow.appendChild(buildKpiCard("Equipment In Use", "15", "📷", "amber", 230));
  statsRow.appendChild(buildKpiCard("Weekly Bookings", "34", "📊", "violet", 230));
  componentsFrame.appendChild(statsRow);

  // Buttons Showcase
  componentsFrame.appendChild(createText("Buttons & Form Actions", 16, "Semi Bold", C.slate900));
  const buttonsRow = figma.createFrame();
  buttonsRow.fills = [];
  applyAutoLayout(buttonsRow, "HORIZONTAL", 16, [0, 0, 0, 0]);
  buttonsRow.appendChild(makeButton("+ Add Employee", "primary"));
  buttonsRow.appendChild(makeButton("Export CSV", "ghost"));
  buttonsRow.appendChild(makeButton("Cancel Action", "ghost"));
  buttonsRow.appendChild(makeButton("Delete Record", "danger"));
  componentsFrame.appendChild(buttonsRow);

  // Form inputs showcase
  componentsFrame.appendChild(createText("Form Elements & PasswordInput (with eye toggle)", 16, "Semi Bold", C.slate900));
  const formRow = figma.createFrame();
  formRow.fills = [];
  applyAutoLayout(formRow, "HORIZONTAL", 20, [0, 0, 0, 0]);
  formRow.appendChild(makeInput("Full Legal Name", "e.g. Maria Santos"));
  formRow.appendChild(makeInput("Email Address", "maria@aevumstudio.com"));
  formRow.appendChild(makeInput("System Password", "••••••••••••", true));
  componentsFrame.appendChild(formRow);

  // Badges showcase
  componentsFrame.appendChild(createText("Status Badges & Chips", 16, "Semi Bold", C.slate900));
  const badgesRow = figma.createFrame();
  badgesRow.fills = [];
  applyAutoLayout(badgesRow, "HORIZONTAL", 12, [0, 0, 0, 0]);
  badgesRow.appendChild(makeBadge("Confirmed", "success"));
  badgesRow.appendChild(makeBadge("Pending Review", "warning"));
  badgesRow.appendChild(makeBadge("In Maintenance", "warning"));
  badgesRow.appendChild(makeBadge("Cancelled", "danger"));
  badgesRow.appendChild(makeBadge("Active Personnel", "info"));
  componentsFrame.appendChild(badgesRow);

  allCreatedFrames.push(componentsFrame);
  canvasX += 1100 + GAP_X;

  // =========================================================================
  // FRAME 3: MODAL - ADD EMPLOYEE WIZARD
  // =========================================================================
  const modalFrame = getOrCreateTopFrame("3. Modal - AddEmployeeWizard", 1100, 1050, canvasX, 0);
  modalFrame.fills = solidFill("#0f172a", 0.7);
  modalFrame.cornerRadius = 16;
  applyAutoLayout(modalFrame, "VERTICAL", 0, [60, 0, 0, 0]);
  modalFrame.primaryAxisAlignItems = "CENTER";
  modalFrame.counterAxisAlignItems = "CENTER";

  const dialog = figma.createFrame();
  dialog.name = "Dialog Box";
  dialog.resize(680, 780);
  dialog.fills = solidFill(C.white);
  dialog.cornerRadius = 16;
  applyAutoLayout(dialog, "VERTICAL", 24, [24, 28, 24, 28]);
  addDropShadow(dialog, 10, 40, 0.25);

  const modalHeader = figma.createFrame();
  modalHeader.resize(624, 48);
  modalHeader.fills = [];
  applyAutoLayout(modalHeader, "HORIZONTAL", 0, [0, 0, 0, 0]);
  modalHeader.counterAxisAlignItems = "CENTER";

  const titleCol = figma.createFrame();
  titleCol.fills = [];
  applyAutoLayout(titleCol, "VERTICAL", 2, [0, 0, 0, 0]);
  titleCol.appendChild(createText("Add Studio Employee", 20, "Bold", C.slate900));
  titleCol.appendChild(createText("Photo Aevum Studio • Staff Onboarding Wizard", 12, "Regular", C.muted));
  modalHeader.appendChild(titleCol);

  const headerSpacer = figma.createFrame();
  headerSpacer.fills = [];
  headerSpacer.resize(320, 10);
  modalHeader.appendChild(headerSpacer);
  modalHeader.appendChild(createText("✕", 18, "Medium", C.muted));
  dialog.appendChild(modalHeader);

  // Stepper
  const stepper = figma.createFrame();
  stepper.resize(624, 44);
  stepper.fills = solidFill(C.slate50);
  stepper.cornerRadius = 10;
  applyAutoLayout(stepper, "HORIZONTAL", 16, [10, 16, 10, 16]);
  stepper.counterAxisAlignItems = "CENTER";

  function makeStep(num, label, isDone = false, isCurrent = false) {
    const st = figma.createFrame();
    st.fills = [];
    applyAutoLayout(st, "HORIZONTAL", 8, [0, 0, 0, 0]);
    st.counterAxisAlignItems = "CENTER";

    const circle = figma.createFrame();
    circle.resize(24, 24);
    circle.cornerRadius = 12;
    circle.fills = (isDone || isCurrent) ? solidFill(C.brand900) : solidFill(C.slate100);
    applyAutoLayout(circle, "VERTICAL", 0, [0, 0, 0, 0]);
    circle.primaryAxisAlignItems = "CENTER";
    circle.counterAxisAlignItems = "CENTER";
    circle.appendChild(createText(isDone ? "✓" : num.toString(), 11, "Bold", (isDone || isCurrent) ? C.white : C.muted));
    st.appendChild(circle);

    st.appendChild(createText(label, 12, isCurrent ? "Bold" : "Medium", isCurrent ? C.slate900 : C.muted));
    return st;
  }

  stepper.appendChild(makeStep(1, "Personal Info", false, true));
  const line1 = figma.createRectangle(); line1.resize(50, 2); line1.fills = solidFill(C.line); stepper.appendChild(line1);
  stepper.appendChild(makeStep(2, "Specializations", false, false));
  const line2 = figma.createRectangle(); line2.resize(50, 2); line2.fills = solidFill(C.line); stepper.appendChild(line2);
  stepper.appendChild(makeStep(3, "Account & Review", false, false));
  dialog.appendChild(stepper);

  const formBody = figma.createFrame();
  formBody.resize(624, 460);
  formBody.fills = [];
  applyAutoLayout(formBody, "VERTICAL", 16, [0, 0, 0, 0]);

  const row1 = figma.createFrame(); row1.fills = []; applyAutoLayout(row1, "HORIZONTAL", 16, [0, 0, 0, 0]);
  row1.appendChild(makeInput("First Name", "e.g. Fernest", false, 300));
  row1.appendChild(makeInput("Last Name", "e.g. Rumon", false, 300));
  formBody.appendChild(row1);

  const row2 = figma.createFrame(); row2.fills = []; applyAutoLayout(row2, "HORIZONTAL", 16, [0, 0, 0, 0]);
  row2.appendChild(makeInput("Official Email Address", "fernest@photoaevum.com", false, 300));
  row2.appendChild(makeInput("Phone Number", "+63 912 345 6789", false, 300));
  formBody.appendChild(row2);

  const row3 = figma.createFrame(); row3.fills = []; applyAutoLayout(row3, "HORIZONTAL", 16, [0, 0, 0, 0]);
  row3.appendChild(makeInput("Primary Role / Title", "Senior Portrait Photographer", false, 300));
  row3.appendChild(makeInput("Initial Temporary Password", "••••••••••••", true, 300));
  formBody.appendChild(row3);

  const specBlock = figma.createFrame();
  specBlock.fills = [];
  applyAutoLayout(specBlock, "VERTICAL", 8, [0, 0, 0, 0]);
  specBlock.appendChild(createText("Initial Specializations & Skills", 12, "Semi Bold", C.slate700));

  const tagsRow = figma.createFrame();
  tagsRow.fills = [];
  applyAutoLayout(tagsRow, "HORIZONTAL", 8, [0, 0, 0, 0]);
  tagsRow.appendChild(makeBadge("Portrait Photography", "info"));
  tagsRow.appendChild(makeBadge("Wedding & Events", "info"));
  tagsRow.appendChild(makeBadge("Studio Lighting", "info"));
  tagsRow.appendChild(makeBadge("Post-Production", "info"));
  specBlock.appendChild(tagsRow);
  formBody.appendChild(specBlock);

  dialog.appendChild(formBody);

  const modalFooter = figma.createFrame();
  modalFooter.resize(624, 52);
  modalFooter.fills = [];
  applyAutoLayout(modalFooter, "HORIZONTAL", 12, [0, 0, 0, 0]);
  modalFooter.counterAxisAlignItems = "CENTER";

  modalFooter.appendChild(makeButton("Cancel", "ghost"));
  const fSpacer = figma.createFrame(); fSpacer.fills = []; fSpacer.resize(360, 10); modalFooter.appendChild(fSpacer);
  modalFooter.appendChild(makeButton("Next Step →", "primary"));
  dialog.appendChild(modalFooter);

  modalFrame.appendChild(dialog);
  allCreatedFrames.push(modalFrame);
  canvasX += 1100 + GAP_X;

  // =========================================================================
  // FRAME 4: SCREEN - ADMIN DASHBOARD (1440x900)
  // =========================================================================
  const adminScreen = getOrCreateTopFrame("4. Screen - Admin Dashboard (/admin)", 1440, 900, canvasX, 0);
  adminScreen.fills = solidFill(C.shellBg);
  applyAutoLayout(adminScreen, "HORIZONTAL", 0, [0, 0, 0, 0]);

  adminScreen.appendChild(buildSidebar("admin", "/admin"));

  const adminContent = figma.createFrame();
  adminContent.name = "Content Area";
  adminContent.resize(1200, 900);
  adminContent.fills = solidFill(C.shellBg);
  applyAutoLayout(adminContent, "VERTICAL", 24, [32, 40, 32, 40]);

  const aHeader = figma.createFrame();
  aHeader.resize(1120, 50);
  aHeader.fills = [];
  applyAutoLayout(aHeader, "HORIZONTAL", 0, [0, 0, 0, 0]);
  aHeader.counterAxisAlignItems = "CENTER";

  const aTitleCol = figma.createFrame();
  aTitleCol.fills = [];
  applyAutoLayout(aTitleCol, "VERTICAL", 2, [0, 0, 0, 0]);
  aTitleCol.appendChild(createText("Dashboard Overview", 24, "Bold", C.slate900));
  aTitleCol.appendChild(createText("Friday, October 23, 2026 • Studio Operations", 13, "Regular", C.muted));
  aHeader.appendChild(aTitleCol);

  const aHSpacer = figma.createFrame(); aHSpacer.fills = []; aHSpacer.resize(650, 10); aHeader.appendChild(aHSpacer);
  aHeader.appendChild(makeButton("+ Quick Booking", "primary"));
  adminContent.appendChild(aHeader);

  const aKpiRow = figma.createFrame();
  aKpiRow.resize(1120, 105);
  aKpiRow.fills = [];
  applyAutoLayout(aKpiRow, "HORIZONTAL", 20, [0, 0, 0, 0]);
  aKpiRow.appendChild(buildKpiCard("Appointments Today", "12", "📅", "teal", 265));
  aKpiRow.appendChild(buildKpiCard("Active Personnel", "8", "👥", "blue", 265));
  aKpiRow.appendChild(buildKpiCard("Equipment In Use", "15", "📷", "amber", 265));
  aKpiRow.appendChild(buildKpiCard("Bookings This Week", "34", "📊", "violet", 265));
  adminContent.appendChild(aKpiRow);

  const tableCard = figma.createFrame();
  tableCard.resize(1120, 480);
  tableCard.fills = solidFill(C.white);
  tableCard.cornerRadius = 14;
  tableCard.strokes = solidFill(C.cardBorder);
  tableCard.strokeWeight = 1;
  applyAutoLayout(tableCard, "VERTICAL", 16, [20, 24, 20, 24]);
  addDropShadow(tableCard, 2, 6, 0.04);

  const tcHeader = figma.createFrame();
  tcHeader.resize(1072, 32);
  tcHeader.fills = [];
  applyAutoLayout(tcHeader, "HORIZONTAL", 0, [0, 0, 0, 0]);
  tcHeader.counterAxisAlignItems = "CENTER";
  tcHeader.appendChild(createText("Recent Bookings & Shoots", 18, "Bold", C.slate900));

  const tcHSpacer = figma.createFrame(); tcHSpacer.fills = []; tcHSpacer.resize(800, 10); tcHeader.appendChild(tcHSpacer);
  tcHeader.appendChild(createText("View All →", 13, "Semi Bold", C.teal));
  tableCard.appendChild(tcHeader);

  function makeTableRow(client, service, datetime, status, statusType = "success", isHeader = false) {
    const row = figma.createFrame();
    row.resize(1072, 44);
    row.fills = isHeader ? solidFill(C.slate50) : solidFill(C.white);
    row.cornerRadius = isHeader ? 6 : 0;
    row.strokes = isHeader ? [] : solidFill(C.line);
    row.strokeWeight = 1;
    applyAutoLayout(row, "HORIZONTAL", 0, [10, 16, 10, 16]);
    row.counterAxisAlignItems = "CENTER";

    function col(txt, w, isBold = false, color = C.slate900) {
      const c = figma.createFrame();
      c.resize(w, 24);
      c.fills = [];
      c.appendChild(createText(txt, isHeader ? 11 : 13, isHeader ? "Bold" : (isBold ? "Semi Bold" : "Regular"), isHeader ? C.muted : color));
      return c;
    }

    row.appendChild(col(client, 280, true));
    row.appendChild(col(service, 320));
    row.appendChild(col(datetime, 260));

    if (isHeader) {
      row.appendChild(col("STATUS", 180));
    } else {
      const bCol = figma.createFrame();
      bCol.resize(180, 26);
      bCol.fills = [];
      bCol.appendChild(makeBadge(status, statusType));
      row.appendChild(bCol);
    }
    return row;
  }

  tableCard.appendChild(makeTableRow("CLIENT", "SERVICE PACKAGE", "DATE & TIME", "STATUS", "success", true));
  tableCard.appendChild(makeTableRow("Eleanor Vance", "Graduation Studio Standard", "Oct 23, 2026 • 10:00 AM", "Confirmed", "success"));
  tableCard.appendChild(makeTableRow("Marcus Rodriguez", "Full Wedding Coverage Package", "Oct 24, 2026 • 02:00 PM", "Pending Review", "warning"));
  tableCard.appendChild(makeTableRow("Dr. Sophia Reyes", "Executive Corporate Headshot", "Oct 25, 2026 • 11:30 AM", "Confirmed", "success"));
  tableCard.appendChild(makeTableRow("Liam & Chloe Chen", "Pre-Nuptial Outdoor Shoot", "Oct 26, 2026 • 04:00 PM", "Confirmed", "success"));
  tableCard.appendChild(makeTableRow("Apex Creative Labs", "Product Commercial Studio Session", "Oct 27, 2026 • 09:00 AM", "Pending Review", "warning"));

  adminContent.appendChild(tableCard);
  adminScreen.appendChild(adminContent);
  allCreatedFrames.push(adminScreen);
  canvasX += 1440 + GAP_X;

  // =========================================================================
  // FRAME 5: SCREEN - STAFF DASHBOARD (/staff)
  // =========================================================================
  const staffScreen = getOrCreateTopFrame("5. Screen - Staff Dashboard (/staff)", 1440, 900, canvasX, 0);
  staffScreen.fills = solidFill(C.shellBg);
  applyAutoLayout(staffScreen, "HORIZONTAL", 0, [0, 0, 0, 0]);

  staffScreen.appendChild(buildSidebar("employee", "/staff"));

  const staffContent = figma.createFrame();
  staffContent.name = "Content Area";
  staffContent.resize(1200, 900);
  staffContent.fills = solidFill(C.shellBg);
  applyAutoLayout(staffContent, "VERTICAL", 24, [32, 40, 32, 40]);

  const sHeader = figma.createFrame();
  sHeader.resize(1120, 50);
  sHeader.fills = [];
  applyAutoLayout(sHeader, "VERTICAL", 2, [0, 0, 0, 0]);
  sHeader.appendChild(createText("Staff Operations Hub", 24, "Bold", C.slate900));
  sHeader.appendChild(createText("Welcome back • Today's Schedule and Assigned Gear", 13, "Regular", C.muted));
  staffContent.appendChild(sHeader);

  const sKpiRow = figma.createFrame();
  sKpiRow.resize(1120, 105);
  sKpiRow.fills = [];
  applyAutoLayout(sKpiRow, "HORIZONTAL", 20, [0, 0, 0, 0]);
  sKpiRow.appendChild(buildKpiCard("Upcoming Shoots", "4", "📅", "teal", 360));
  sKpiRow.appendChild(buildKpiCard("Completed This Week", "9", "✓", "blue", 360));
  sKpiRow.appendChild(buildKpiCard("Gear Checked Out", "3", "📷", "amber", 360));
  staffContent.appendChild(sKpiRow);

  staffContent.appendChild(createText("Quick Workflows", 18, "Bold", C.slate900));
  const sNavRow = figma.createFrame();
  sNavRow.resize(1120, 130);
  sNavRow.fills = [];
  applyAutoLayout(sNavRow, "HORIZONTAL", 20, [0, 0, 0, 0]);
  sNavRow.appendChild(makeActionCard("My Shoot Assignments", "View upcoming call times, customer location briefs, and shot lists.", "📋"));
  sNavRow.appendChild(makeActionCard("Delivery Pipeline", "Track photo progress stages: Culling, Color Grading, and Gallery Upload.", "🔄"));
  sNavRow.appendChild(makeActionCard("Gear Checklist", "Check out studio bodies/lenses and report hardware maintenance.", "📷"));
  staffContent.appendChild(sNavRow);

  const availCard = figma.createFrame();
  availCard.resize(1120, 280);
  availCard.fills = solidFill(C.white);
  availCard.cornerRadius = 14;
  availCard.strokes = solidFill(C.cardBorder);
  availCard.strokeWeight = 1;
  applyAutoLayout(availCard, "VERTICAL", 16, [24, 28, 24, 28]);
  addDropShadow(availCard, 2, 6, 0.04);

  availCard.appendChild(createText("Availability & Schedule Blocking", 18, "Bold", C.slate900));
  availCard.appendChild(createText("Mark off-duty or unavailable dates so studio dispatch doesn't assign shoots during your blackout hours.", 13, "Regular", C.muted));

  const availGrid = figma.createFrame();
  availGrid.fills = solidFill(C.slate50);
  availGrid.cornerRadius = 10;
  applyAutoLayout(availGrid, "HORIZONTAL", 16, [16, 20, 16, 20]);
  availGrid.appendChild(createText("Mon: 09:00 - 18:00 (Available)", 13, "Semi Bold", C.emerald));
  availGrid.appendChild(createText("Tue: 09:00 - 18:00 (Available)", 13, "Semi Bold", C.emerald));
  availGrid.appendChild(createText("Wed: Blocked (Off-Duty)", 13, "Semi Bold", C.amber));
  availGrid.appendChild(createText("Thu: 13:00 - 20:00 (Evening)", 13, "Semi Bold", C.teal));
  availGrid.appendChild(createText("Fri: 09:00 - 18:00 (Available)", 13, "Semi Bold", C.emerald));
  availCard.appendChild(availGrid);

  staffContent.appendChild(availCard);
  staffScreen.appendChild(staffContent);
  allCreatedFrames.push(staffScreen);
  canvasX += 1440 + GAP_X;

  // =========================================================================
  // FRAME 6: SCREEN - CUSTOMER PORTAL (/my)
  // =========================================================================
  const custScreen = getOrCreateTopFrame("6. Screen - Customer Portal (/my)", 1440, 900, canvasX, 0);
  custScreen.fills = solidFill(C.shellBg);
  applyAutoLayout(custScreen, "HORIZONTAL", 0, [0, 0, 0, 0]);

  custScreen.appendChild(buildSidebar("customer", "/my"));

  const custContent = figma.createFrame();
  custContent.name = "Content Area";
  custContent.resize(1200, 900);
  custContent.fills = solidFill(C.shellBg);
  applyAutoLayout(custContent, "VERTICAL", 24, [32, 40, 32, 40]);

  const cHeader = figma.createFrame();
  cHeader.resize(1120, 50);
  cHeader.fills = [];
  applyAutoLayout(cHeader, "HORIZONTAL", 0, [0, 0, 0, 0]);
  cHeader.counterAxisAlignItems = "CENTER";

  const cTitleCol = figma.createFrame();
  cTitleCol.fills = [];
  applyAutoLayout(cTitleCol, "VERTICAL", 2, [0, 0, 0, 0]);
  cTitleCol.appendChild(createText("Welcome to Aevum", 24, "Bold", C.slate900));
  cTitleCol.appendChild(createText("Your personal photography sessions & gallery hub", 13, "Regular", C.muted));
  cHeader.appendChild(cTitleCol);

  const cHSpacer = figma.createFrame(); cHSpacer.fills = []; cHSpacer.resize(650, 10); cHeader.appendChild(cHSpacer);
  cHeader.appendChild(makeButton("+ Book a Session", "primary"));
  custContent.appendChild(cHeader);

  const alertBanner = figma.createFrame();
  alertBanner.resize(1120, 48);
  alertBanner.cornerRadius = 10;
  alertBanner.fills = solidFill(C.amberBg);
  alertBanner.strokes = solidFill(C.amber);
  alertBanner.strokeWeight = 1;
  applyAutoLayout(alertBanner, "HORIZONTAL", 12, [12, 20, 12, 20]);
  alertBanner.counterAxisAlignItems = "CENTER";
  alertBanner.appendChild(createText("🔔", 16, "Regular", C.amber));
  alertBanner.appendChild(createText("You have 1 pending session — Please review your graduation shoot schedule for tomorrow.", 13, "Semi Bold", C.slate900));
  custContent.appendChild(alertBanner);

  const cNavRow = figma.createFrame();
  cNavRow.resize(1120, 130);
  cNavRow.fills = [];
  applyAutoLayout(cNavRow, "HORIZONTAL", 20, [0, 0, 0, 0]);
  cNavRow.appendChild(makeActionCard("My Appointments", "Check appointment time, studio room, and photographer details.", "📋"));
  cNavRow.appendChild(makeActionCard("Book a Schedule", "Reserve a portrait, event, graduation, or commercial session.", "➕"));
  cNavRow.appendChild(makeActionCard("Private Galleries", "Review high-resolution proofs, favourite selects, and download files.", "🖼"));
  custContent.appendChild(cNavRow);

  const cTableCard = figma.createFrame();
  cTableCard.resize(1120, 360);
  cTableCard.fills = solidFill(C.white);
  cTableCard.cornerRadius = 14;
  cTableCard.strokes = solidFill(C.cardBorder);
  cTableCard.strokeWeight = 1;
  applyAutoLayout(cTableCard, "VERTICAL", 16, [20, 24, 20, 24]);
  addDropShadow(cTableCard, 2, 6, 0.04);

  cTableCard.appendChild(createText("Your Upcoming Sessions", 18, "Bold", C.slate900));
  cTableCard.appendChild(makeTableRow("SERVICE", "LOCATION / STUDIO", "DATE & TIME", "STATUS", "success", true));
  cTableCard.appendChild(makeTableRow("Studio Graduation Portrait", "Studio Bay A (Main Campus)", "Tomorrow • 10:00 AM", "Confirmed", "success"));
  cTableCard.appendChild(makeTableRow("Outdoor Family Shoot", "Emerald Garden Conservatory", "Nov 14, 2026 • 03:30 PM", "Pending Review", "warning"));

  custContent.appendChild(cTableCard);
  custScreen.appendChild(custContent);
  allCreatedFrames.push(custScreen);
  canvasX += 1440 + GAP_X;

  // =========================================================================
  // FRAME 7: SCREEN - STAFF DIRECTORY (Employee.razor)
  // =========================================================================
  const empScreen = getOrCreateTopFrame("7. Screen - Staff Directory (/admin/employees)", 1440, 900, canvasX, 0);
  empScreen.fills = solidFill(C.shellBg);
  applyAutoLayout(empScreen, "HORIZONTAL", 0, [0, 0, 0, 0]);

  empScreen.appendChild(buildSidebar("admin", "/admin/employees"));

  const empContent = figma.createFrame();
  empContent.name = "Content Area";
  empContent.resize(1200, 900);
  empContent.fills = solidFill(C.shellBg);
  applyAutoLayout(empContent, "VERTICAL", 24, [32, 40, 32, 40]);

  const eHeader = figma.createFrame();
  eHeader.resize(1120, 50);
  eHeader.fills = [];
  applyAutoLayout(eHeader, "HORIZONTAL", 0, [0, 0, 0, 0]);
  eHeader.counterAxisAlignItems = "CENTER";

  const eTitleCol = figma.createFrame();
  eTitleCol.fills = [];
  applyAutoLayout(eTitleCol, "VERTICAL", 2, [0, 0, 0, 0]);
  eTitleCol.appendChild(createText("Staff Management", 24, "Bold", C.slate900));
  eTitleCol.appendChild(createText("Manage photo studio personnel, active specializations, and system accounts.", 13, "Regular", C.muted));
  eHeader.appendChild(eTitleCol);

  const eHSpacer = figma.createFrame(); eHSpacer.fills = []; eHSpacer.resize(600, 10); eHeader.appendChild(eHSpacer);
  eHeader.appendChild(makeButton("+ Add Employee", "primary"));
  empContent.appendChild(eHeader);

  const eKpiRow = figma.createFrame();
  eKpiRow.resize(1120, 90);
  eKpiRow.fills = [];
  applyAutoLayout(eKpiRow, "HORIZONTAL", 20, [0, 0, 0, 0]);
  eKpiRow.appendChild(buildKpiCard("Total Staff", "14", "👥", "teal", 360));
  eKpiRow.appendChild(buildKpiCard("Active Personnel", "12", "✓", "emerald", 360));
  eKpiRow.appendChild(buildKpiCard("Unique Specializations", "6", "★", "amber", 360));
  empContent.appendChild(eKpiRow);

  const empTableCard = figma.createFrame();
  empTableCard.resize(1120, 560);
  empTableCard.fills = solidFill(C.white);
  empTableCard.cornerRadius = 14;
  empTableCard.strokes = solidFill(C.cardBorder);
  empTableCard.strokeWeight = 1;
  applyAutoLayout(empTableCard, "VERTICAL", 14, [20, 24, 20, 24]);
  addDropShadow(empTableCard, 2, 6, 0.04);

  function makeStaffRow(name, email, role, specs, status = "Active", isHeader = false) {
    const row = figma.createFrame();
    row.resize(1072, 48);
    row.fills = isHeader ? solidFill(C.slate50) : solidFill(C.white);
    row.strokes = isHeader ? [] : solidFill(C.line);
    row.strokeWeight = 1;
    applyAutoLayout(row, "HORIZONTAL", 0, [8, 16, 8, 16]);
    row.counterAxisAlignItems = "CENTER";

    function col(txt, w, isBold = false) {
      const c = figma.createFrame();
      c.resize(w, 24);
      c.fills = [];
      c.appendChild(createText(txt, isHeader ? 11 : 13, isHeader ? "Bold" : (isBold ? "Semi Bold" : "Regular"), isHeader ? C.muted : C.slate900));
      return c;
    }

    if (isHeader) {
      row.appendChild(col("NAME & CONTACT", 320));
      row.appendChild(col("ROLE / TITLE", 220));
      row.appendChild(col("SPECIALIZATIONS", 380));
      row.appendChild(col("STATUS", 152));
    } else {
      const nameCol = figma.createFrame();
      nameCol.resize(320, 36);
      nameCol.fills = [];
      applyAutoLayout(nameCol, "HORIZONTAL", 10, [0, 0, 0, 0]);
      nameCol.counterAxisAlignItems = "CENTER";

      const av = figma.createFrame();
      av.resize(32, 32);
      av.cornerRadius = 16;
      av.fills = solidFill(C.slate100);
      applyAutoLayout(av, "VERTICAL", 0, [0, 0, 0, 0]);
      av.primaryAxisAlignItems = "CENTER";
      av.counterAxisAlignItems = "CENTER";
      av.appendChild(createText(name.split(" ").map(n => n[0]).join(""), 11, "Bold", C.slate700));
      nameCol.appendChild(av);

      const sub = figma.createFrame();
      sub.fills = [];
      applyAutoLayout(sub, "VERTICAL", 2, [0, 0, 0, 0]);
      sub.appendChild(createText(name, 13, "Semi Bold", C.slate900));
      sub.appendChild(createText(email, 11, "Regular", C.muted));
      nameCol.appendChild(sub);
      row.appendChild(nameCol);

      row.appendChild(col(role, 220));

      const specCol = figma.createFrame();
      specCol.resize(380, 28);
      specCol.fills = [];
      applyAutoLayout(specCol, "HORIZONTAL", 6, [0, 0, 0, 0]);
      for (const s of specs) {
        specCol.appendChild(makeBadge(s, "info"));
      }
      row.appendChild(specCol);

      const stCol = figma.createFrame();
      stCol.resize(152, 28);
      stCol.fills = [];
      stCol.appendChild(makeBadge(status, status === "Active" ? "success" : "danger"));
      row.appendChild(stCol);
    }
    return row;
  }

  empTableCard.appendChild(makeStaffRow("STAFF MEMBER", "ROLE", "SPECIALIZATIONS", "STATUS", "", true));
  empTableCard.appendChild(makeStaffRow("Fernest Bac", "fernest@aevumstudio.com", "Lead Studio Director", ["Portrait", "Commercial", "Lighting"]));
  empTableCard.appendChild(makeStaffRow("Samantha Hayes", "samantha@aevumstudio.com", "Senior Event Photographer", ["Wedding", "Events"]));
  empTableCard.appendChild(makeStaffRow("David Kim", "david@aevumstudio.com", "Portrait Specialist", ["Graduation", "Studio Portrait"]));
  empTableCard.appendChild(makeStaffRow("Rachel Cruz", "rachel@aevumstudio.com", "Digital Media & Retoucher", ["Post-Processing", "Color"]));
  empTableCard.appendChild(makeStaffRow("Carlos Mendoza", "carlos@aevumstudio.com", "Assistant Photographer", ["Lighting", "Backstage"]));

  empContent.appendChild(empTableCard);
  empScreen.appendChild(empContent);
  allCreatedFrames.push(empScreen);
  canvasX += 1440 + GAP_X;

  // =========================================================================
  // FRAME 8: SCREEN - CLIENT DIRECTORY (Clients.razor)
  // =========================================================================
  const clientScreen = getOrCreateTopFrame("8. Screen - Client Directory (/admin/clients)", 1440, 900, canvasX, 0);
  clientScreen.fills = solidFill(C.shellBg);
  applyAutoLayout(clientScreen, "HORIZONTAL", 0, [0, 0, 0, 0]);

  clientScreen.appendChild(buildSidebar("admin", "/admin/clients"));

  const clientContent = figma.createFrame();
  clientContent.name = "Content Area";
  clientContent.resize(1200, 900);
  clientContent.fills = solidFill(C.shellBg);
  applyAutoLayout(clientContent, "VERTICAL", 24, [32, 40, 32, 40]);

  const clHeader = figma.createFrame();
  clHeader.resize(1120, 50);
  clHeader.fills = [];
  applyAutoLayout(clHeader, "HORIZONTAL", 0, [0, 0, 0, 0]);
  clHeader.counterAxisAlignItems = "CENTER";

  const clTitleCol = figma.createFrame();
  clTitleCol.fills = [];
  applyAutoLayout(clTitleCol, "VERTICAL", 2, [0, 0, 0, 0]);
  clTitleCol.appendChild(createText("Clients Directory", 24, "Bold", C.slate900));
  clTitleCol.appendChild(createText("Directory of registered studio clients, contact details, and account histories.", 13, "Regular", C.muted));
  clHeader.appendChild(clTitleCol);

  const clHSpacer = figma.createFrame(); clHSpacer.fills = []; clHSpacer.resize(650, 10); clHeader.appendChild(clHSpacer);
  clHeader.appendChild(makeButton("Export Clients", "ghost"));
  clientContent.appendChild(clHeader);

  const clTableCard = figma.createFrame();
  clTableCard.resize(1120, 600);
  clTableCard.fills = solidFill(C.white);
  clTableCard.cornerRadius = 14;
  clTableCard.strokes = solidFill(C.cardBorder);
  clTableCard.strokeWeight = 1;
  applyAutoLayout(clTableCard, "VERTICAL", 14, [20, 24, 20, 24]);
  addDropShadow(clTableCard, 2, 6, 0.04);

  function makeClientRow(id, name, email, phone, isHeader = false) {
    const row = figma.createFrame();
    row.resize(1072, 46);
    row.fills = isHeader ? solidFill(C.slate50) : solidFill(C.white);
    row.strokes = isHeader ? [] : solidFill(C.line);
    row.strokeWeight = 1;
    applyAutoLayout(row, "HORIZONTAL", 0, [10, 16, 10, 16]);
    row.counterAxisAlignItems = "CENTER";

    function col(txt, w, isMono = false, isBold = false) {
      const c = figma.createFrame();
      c.resize(w, 24);
      c.fills = [];
      c.appendChild(createText(txt, isHeader ? 11 : 13, isHeader ? "Bold" : (isBold ? "Semi Bold" : "Regular"), isHeader ? C.muted : (isMono ? C.slate500 : C.slate900)));
      return c;
    }

    row.appendChild(col(id, 160, true));
    row.appendChild(col(name, 320, false, true));
    row.appendChild(col(email, 360));
    row.appendChild(col(phone, 232));
    return row;
  }

  clTableCard.appendChild(makeClientRow("CLIENT ID", "CLIENT NAME", "EMAIL ADDRESS", "PHONE NUMBER", true));
  clTableCard.appendChild(makeClientRow("CLI-0842", "Eleanor Vance", "eleanor.vance@gmail.com", "+63 917 882 1104"));
  clTableCard.appendChild(makeClientRow("CLI-0843", "Marcus Rodriguez", "marcus.rod@outlook.com", "+63 920 445 9931"));
  clTableCard.appendChild(makeClientRow("CLI-0844", "Dr. Sophia Reyes", "dr.reyes@medcenter.org", "+63 908 123 4567"));
  clTableCard.appendChild(makeClientRow("CLI-0845", "Liam Chen", "liam.chen@creativehub.com", "+63 918 555 7820"));
  clTableCard.appendChild(makeClientRow("CLI-0846", "Apex Creative Labs", "ops@apexcreativelabs.io", "+63 2 8876 5432"));
  clTableCard.appendChild(makeClientRow("CLI-0847", "Isabella Gomez", "isabella.g@yahoo.com", "+63 927 334 1198"));

  clientContent.appendChild(clTableCard);
  clientScreen.appendChild(clientContent);
  allCreatedFrames.push(clientScreen);
  canvasX += 1440 + GAP_X;

  // =========================================================================
  // FRAME 9: SCREEN - CUSTOMER BOOKING FLOW (/my/book)
  // =========================================================================
  const bookScreen = getOrCreateTopFrame("9. Screen - Customer Booking Flow (/my/book)", 1440, 950, canvasX, 0);
  bookScreen.fills = solidFill(C.shellBg);
  applyAutoLayout(bookScreen, "HORIZONTAL", 0, [0, 0, 0, 0]);

  bookScreen.appendChild(buildSidebar("customer", "/my/book"));

  const bookContent = figma.createFrame();
  bookContent.name = "Content Area";
  bookContent.resize(1200, 950);
  bookContent.fills = solidFill(C.shellBg);
  applyAutoLayout(bookContent, "VERTICAL", 24, [32, 40, 32, 40]);

  // Booking Header
  const bHeader = figma.createFrame();
  bHeader.resize(1120, 60);
  bHeader.fills = [];
  applyAutoLayout(bHeader, "VERTICAL", 4, [0, 0, 0, 0]);
  bHeader.appendChild(createText("Online Studio Scheduling", 11, "Bold", C.teal));
  bHeader.appendChild(createText("Book a Studio Session", 24, "Bold", C.slate900));
  bookContent.appendChild(bHeader);

  // Stepper Header
  const bStepper = figma.createFrame();
  bStepper.resize(1120, 48);
  bStepper.fills = solidFill(C.white);
  bStepper.cornerRadius = 12;
  bStepper.strokes = solidFill(C.cardBorder);
  bStepper.strokeWeight = 1;
  applyAutoLayout(bStepper, "HORIZONTAL", 24, [12, 24, 12, 24]);
  bStepper.counterAxisAlignItems = "CENTER";
  bStepper.appendChild(makeStep(1, "1. Select Studio Service", false, true));
  const bLine1 = figma.createRectangle(); bLine1.resize(60, 2); bLine1.fills = solidFill(C.line); bStepper.appendChild(bLine1);
  bStepper.appendChild(makeStep(2, "2. Pick Date & Available Slot", false, false));
  const bLine2 = figma.createRectangle(); bLine2.resize(60, 2); bLine2.fills = solidFill(C.line); bStepper.appendChild(bLine2);
  bStepper.appendChild(makeStep(3, "3. Add-ons & Customization", false, false));
  const bLine3 = figma.createRectangle(); bLine3.resize(60, 2); bLine3.fills = solidFill(C.line); bStepper.appendChild(bLine3);
  bStepper.appendChild(makeStep(4, "4. Review & Confirm", false, false));
  bookContent.appendChild(bStepper);

  // Service Selection Grid
  const serviceGrid = figma.createFrame();
  serviceGrid.resize(1120, 650);
  serviceGrid.fills = [];
  applyAutoLayout(serviceGrid, "HORIZONTAL", 20, [0, 0, 0, 0]);

  function makeServiceCard(title, price, duration, desc, isSelected = false) {
    const sc = figma.createFrame();
    sc.resize(355, 340);
    sc.fills = solidFill(C.white);
    sc.cornerRadius = 14;
    sc.strokes = solidFill(isSelected ? C.teal : C.cardBorder);
    sc.strokeWeight = isSelected ? 2 : 1;
    applyAutoLayout(sc, "VERTICAL", 12, [20, 20, 20, 20]);
    addDropShadow(sc, 2, 8, isSelected ? 0.08 : 0.04);

    const imgThumb = figma.createFrame();
    imgThumb.resize(315, 130);
    imgThumb.cornerRadius = 8;
    imgThumb.fills = solidFill(isSelected ? "#e6fffa" : C.slate100);
    applyAutoLayout(imgThumb, "VERTICAL", 0, [0, 0, 0, 0]);
    imgThumb.primaryAxisAlignItems = "CENTER";
    imgThumb.counterAxisAlignItems = "CENTER";
    imgThumb.appendChild(createText("📷 Studio Showcase", 13, "Semi Bold", isSelected ? C.teal : C.muted));
    sc.appendChild(imgThumb);

    const titleRow = figma.createFrame();
    titleRow.resize(315, 24);
    titleRow.fills = [];
    applyAutoLayout(titleRow, "HORIZONTAL", 0, [0, 0, 0, 0]);
    titleRow.appendChild(createText(title, 16, "Bold", C.slate900));
    sc.appendChild(titleRow);

    sc.appendChild(createText(desc, 12, "Regular", C.muted));

    const priceRow = figma.createFrame();
    priceRow.resize(315, 28);
    priceRow.fills = [];
    applyAutoLayout(priceRow, "HORIZONTAL", 0, [0, 0, 0, 0]);
    priceRow.counterAxisAlignItems = "CENTER";
    priceRow.appendChild(createText(price, 18, "Bold", C.slate900));
    
    const pSpacer = figma.createFrame(); pSpacer.fills = []; pSpacer.resize(150, 10); priceRow.appendChild(pSpacer);
    priceRow.appendChild(createText(duration, 12, "Medium", C.muted));
    sc.appendChild(priceRow);

    const selectBtn = figma.createFrame();
    selectBtn.resize(315, 38);
    selectBtn.cornerRadius = 8;
    selectBtn.fills = isSelected ? solidFill(C.teal) : solidFill(C.brand900);
    applyAutoLayout(selectBtn, "VERTICAL", 0, [0, 0, 0, 0]);
    selectBtn.primaryAxisAlignItems = "CENTER";
    selectBtn.counterAxisAlignItems = "CENTER";
    selectBtn.appendChild(createText(isSelected ? "Selected ✓" : "Select Service", 12, "Bold", C.white));
    sc.appendChild(selectBtn);

    return sc;
  }

  serviceGrid.appendChild(makeServiceCard("Graduation Studio Portrait", "₱ 2,500", "45 mins", "Full academic regalia setup, 3 backdrops, and 5 master retouched digital copies.", true));
  serviceGrid.appendChild(makeServiceCard("Corporate & Executive Headshot", "₱ 3,200", "60 mins", "Tailored studio lighting for LinkedIn, corporate bios, and commercial press releases."));
  serviceGrid.appendChild(makeServiceCard("Full Wedding Studio Pre-Nup", "₱ 15,000", "3 hours", "Comprehensive romantic session with wardrobe changes and personalized cinematic lighting."));

  bookContent.appendChild(serviceGrid);
  bookScreen.appendChild(bookContent);
  allCreatedFrames.push(bookScreen);
  canvasX += 1440 + GAP_X;

  // =========================================================================
  // FRAME 10: SCREEN - PUBLIC LANDING PAGE (Home.razor & NavMenu.razor)
  // =========================================================================
  const homeScreen = getOrCreateTopFrame("10. Screen - Public Home & Landing (Home.razor)", 1440, 1100, canvasX, 0);
  homeScreen.fills = solidFill(C.white);
  applyAutoLayout(homeScreen, "VERTICAL", 0, [0, 0, 0, 0]);

  // Glass Nav
  const glassNav = figma.createFrame();
  glassNav.name = "Glass Nav (glass-nav)";
  glassNav.resize(1440, 80);
  glassNav.fills = solidFill(C.white, 0.95);
  glassNav.strokes = solidFill(C.line);
  glassNav.strokeWeight = 1;
  applyAutoLayout(glassNav, "HORIZONTAL", 0, [20, 80, 20, 80]);
  glassNav.counterAxisAlignItems = "CENTER";

  const hBrand = figma.createFrame();
  hBrand.fills = [];
  applyAutoLayout(hBrand, "HORIZONTAL", 12, [0, 0, 0, 0]);
  hBrand.counterAxisAlignItems = "CENTER";
  const hLogo = figma.createEllipse(); hLogo.resize(40, 40); hLogo.fills = solidFill(C.brand900); hBrand.appendChild(hLogo);
  hBrand.appendChild(createText("PHOTO AEVUM STUDIO", 15, "Bold", C.slate900));
  glassNav.appendChild(hBrand);

  const navSpacer = figma.createFrame(); navSpacer.fills = []; navSpacer.resize(600, 10); glassNav.appendChild(navSpacer);

  const navLinks = figma.createFrame();
  navLinks.fills = [];
  applyAutoLayout(navLinks, "HORIZONTAL", 28, [0, 0, 0, 0]);
  navLinks.counterAxisAlignItems = "CENTER";
  navLinks.appendChild(createText("HOME", 12, "Bold", C.slate900));
  navLinks.appendChild(createText("SERVICES", 12, "Medium", C.muted));
  navLinks.appendChild(createText("PORTFOLIO", 12, "Medium", C.muted));
  navLinks.appendChild(createText("ABOUT", 12, "Medium", C.muted));
  navLinks.appendChild(createText("CONTACT", 12, "Medium", C.muted));
  navLinks.appendChild(makeButton("Client Portal →", "primary"));
  glassNav.appendChild(navLinks);
  homeScreen.appendChild(glassNav);

  // Hero Section
  const heroSection = figma.createFrame();
  heroSection.resize(1440, 520);
  heroSection.fills = solidFill(C.slate50);
  applyAutoLayout(heroSection, "VERTICAL", 20, [80, 80, 80, 80]);
  heroSection.counterAxisAlignItems = "CENTER";

  heroSection.appendChild(createText("MODERN PHOTOGRAPHY & CINEMATIC PORTRAITURE", 12, "Bold", C.teal));

  const heroTitle = createText("Capturing Timeless Moments With Modern Precision", 42, "Bold", C.slate900);
  heroSection.appendChild(heroTitle);

  const heroDesc = createText("Photo Aevum Studio blends cutting-edge lighting design with timeless aesthetics for portraiture, corporate milestones, weddings, and editorial campaigns.", 16, "Regular", C.muted);
  heroSection.appendChild(heroDesc);

  const heroActions = figma.createFrame();
  heroActions.fills = [];
  applyAutoLayout(heroActions, "HORIZONTAL", 16, [16, 0, 0, 0]);
  heroActions.appendChild(makeButton("Book Your Session Now", "primary"));
  heroActions.appendChild(makeButton("Explore Portfolio", "ghost"));
  heroSection.appendChild(heroActions);
  homeScreen.appendChild(heroSection);

  // Services Grid Preview
  const servicesSection = figma.createFrame();
  servicesSection.resize(1440, 360);
  servicesSection.fills = solidFill(C.white);
  applyAutoLayout(servicesSection, "VERTICAL", 24, [40, 80, 40, 80]);
  servicesSection.appendChild(createText("Our Studio Services", 24, "Bold", C.slate900));

  const sRow = figma.createFrame();
  sRow.resize(1280, 220);
  sRow.fills = [];
  applyAutoLayout(sRow, "HORIZONTAL", 24, [0, 0, 0, 0]);

  function makeLandingCard(title, desc) {
    const card = figma.createFrame();
    card.resize(400, 200);
    card.fills = solidFill(C.white);
    card.cornerRadius = 12;
    card.strokes = solidFill(C.line);
    card.strokeWeight = 1;
    applyAutoLayout(card, "VERTICAL", 10, [24, 24, 24, 24]);
    card.appendChild(createText(title, 18, "Bold", C.slate900));
    card.appendChild(createText(desc, 13, "Regular", C.muted));
    card.appendChild(createText("Learn more →", 13, "Semi Bold", C.teal));
    return card;
  }

  sRow.appendChild(makeLandingCard("Portrait & Graduation", "Studio portraits crafted with intentional lighting to highlight character and achievement."));
  sRow.appendChild(makeLandingCard("Weddings & Pre-Nups", "Cinematic storytelling capturing spontaneous joy, intimate glances, and grand celebrations."));
  sRow.appendChild(makeLandingCard("Corporate & Commercial", "Executive branding, product photography, and high-impact editorial imagery for brands."));
  servicesSection.appendChild(sRow);
  homeScreen.appendChild(servicesSection);

  // Footer
  const footer = figma.createFrame();
  footer.resize(1440, 140);
  footer.fills = solidFill(C.brand900);
  applyAutoLayout(footer, "HORIZONTAL", 0, [40, 80, 40, 80]);
  footer.counterAxisAlignItems = "CENTER";
  footer.appendChild(createText("© 2026 Photo Aevum Studio. All rights reserved.", 13, "Regular", C.slate400));
  const footSpacer = figma.createFrame(); footSpacer.fills = []; footSpacer.resize(750, 10); footer.appendChild(footSpacer);
  footer.appendChild(createText("Privacy Policy  •  Terms of Service  •  Support Portal", 13, "Regular", C.slate400));
  homeScreen.appendChild(footer);

  allCreatedFrames.push(homeScreen);
  canvasX += 1440 + GAP_X;

  // Zoom into all generated frames
  figma.viewport.scrollAndZoomIntoView(allCreatedFrames);

  const summaryMsg = updatedCount > 0
    ? "Aevum UI updated in-place (" + updatedCount + " updated, " + createdCount + " created)!"
    : "Aevum UI generated (" + createdCount + " frames created)!";

  figma.notify(summaryMsg, { timeout: 5000 });
})();
