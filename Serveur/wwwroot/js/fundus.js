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
document.querySelectorAll("form[method=post]").forEach(form => form.addEventListener("submit", () => {
    form.querySelectorAll("button[type=submit]").forEach(button => {
        button.disabled = true;
        button.textContent = form.classList.contains("fo-refresh") ? "Analyse en cours…" : "Enregistrement…";
    });
}));
const source = document.getElementById("consultation-source");
if (source) { source.scrollIntoView(); source.focus({preventScroll:true}); }
