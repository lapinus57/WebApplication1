"use strict";
document.querySelectorAll(".fo-map-group").forEach(group => {
    const table = group.querySelector(".fo-table");
    function update(changed) {
        group.querySelectorAll(".fo-column").forEach(column => {
            for (const option of column.options) {
                const hidden = !!option.dataset.table && option.dataset.table !== table.value;
                option.hidden = hidden;
                option.disabled = hidden;
                if (hidden || changed) option.selected = false;
            }
            if (changed && !column.multiple) column.value = "";
        });
    }
    table.addEventListener("change", () => update(true));
    update(false);
});
// Filtering patient names stays entirely in this page: no names in URLs or request logs.
const search = document.getElementById("fo-patient-search");
const rows = Array.from(document.querySelectorAll(".fo-patient-row"));
const normalize = value => value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("fr").trim();
if (search) search.addEventListener("input", () => {
    const query = normalize(search.value);
    let visible = 0;
    rows.forEach(row => {
        row.hidden = !normalize(row.querySelector(".fo-patient-identity").textContent).includes(query);
        if (!row.hidden) visible++;
    });
    document.getElementById("fo-list-count").textContent = `${visible} patient(s) affiché(s) sur ${rows.length}`;
    document.getElementById("fo-no-match").hidden = visible !== 0;
});

document.querySelectorAll("form[method=post]").forEach(form => form.addEventListener("submit", () => {
    form.querySelectorAll("button[type=submit]").forEach(button => {
        button.disabled = true;
        button.textContent = form.classList.contains("fo-refresh") ? "Recherche en cours…" : "Enregistrement…";
    });
    if (form.classList.contains("fo-refresh")) {
        document.getElementById("fo-search-progress").hidden = false;
        document.getElementById("fo-patient-list")?.setAttribute("aria-busy", "true");
        if (search) search.disabled = true;
    }
}));
const source = document.getElementById("consultation-source");
if (source) { source.scrollIntoView(); source.focus({preventScroll:true}); }

// Automatic analysis is a single authenticated, antiforgery-protected POST.
// Search/review responses and source/configuration views never carry this flag, preventing a reload loop.
// Saving configuration opens the list and schedules its first search.
const searchForm = document.getElementById("fo-search");
if (searchForm?.dataset.autoSearch === "true") searchForm.requestSubmit();
