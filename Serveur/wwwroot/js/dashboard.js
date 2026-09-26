(() => {
    const dashboard = document.querySelector("[data-dashboard]");
    if (!dashboard) return;

    const refreshButton = dashboard.querySelector("[data-refresh]");
    const numberFormat = new Intl.NumberFormat("fr-FR");

    const setText = (selector, value) => {
        const element = dashboard.querySelector(selector);
        if (element) element.textContent = value;
    };

    const renderUsers = (users) => {
        const list = dashboard.querySelector("[data-user-list]");
        if (!list) return;

        list.replaceChildren();
        if (!users.length) {
            const empty = document.createElement("div");
            empty.className = "empty-state";
            empty.innerHTML = "<span aria-hidden=\"true\">◎</span><p>Aucun client connecté pour le moment.</p>";
            list.append(empty);
            return;
        }

        users.forEach((user) => {
            const row = document.createElement("div");
            row.className = "user-row";

            const avatar = document.createElement("span");
            avatar.className = "user-avatar";
            avatar.textContent = user.name.slice(0, 1).toLocaleUpperCase("fr-FR");

            const identity = document.createElement("span");
            const name = document.createElement("strong");
            const room = document.createElement("small");
            name.textContent = user.name;
            room.textContent = user.room || "Salle non renseignée";
            identity.append(name, room);

            const badge = document.createElement("span");
            badge.className = "online-badge";
            badge.textContent = "En ligne";
            row.append(avatar, identity, badge);
            list.append(row);
        });
    };

    const render = (snapshot) => {
        setText("[data-status]", snapshot.status);
        setText("[data-connected-users]", numberFormat.format(snapshot.connectedUsers));
        setText("[data-known-users]", numberFormat.format(snapshot.knownUsers));
        setText("[data-messages]", numberFormat.format(snapshot.messages));
        setText("[data-active-patients]", numberFormat.format(snapshot.activePatients));
        setText("[data-memory]", `${numberFormat.format(Math.round(snapshot.memoryBytes / 1024 / 1024))} Mo`);
        setText("[data-database-status]", snapshot.isHealthy ? "Connectée" : "Indisponible");
        setText("[data-checked-at]", new Date(snapshot.checkedAt).toLocaleTimeString("fr-FR"));
        setText("[data-started-at]", new Date(snapshot.startedAt).toLocaleString("fr-FR", { dateStyle: "short", timeStyle: "short" }));

        const pill = dashboard.querySelector("[data-status-pill]");
        pill?.classList.toggle("is-online", snapshot.isHealthy);
        pill?.classList.toggle("is-offline", !snapshot.isHealthy);
        renderUsers(snapshot.users);
    };

    const refresh = async () => {
        refreshButton?.setAttribute("disabled", "");
        refreshButton?.classList.add("is-loading");
        try {
            const response = await fetch(dashboard.dataset.snapshotUrl, {
                headers: { "Accept": "application/json" },
                cache: "no-store"
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            render(await response.json());
        } catch (error) {
            console.error("Actualisation du tableau de bord impossible.", error);
            setText("[data-status]", "Actualisation impossible");
            const pill = dashboard.querySelector("[data-status-pill]");
            pill?.classList.remove("is-online");
            pill?.classList.add("is-offline");
        } finally {
            refreshButton?.removeAttribute("disabled");
            refreshButton?.classList.remove("is-loading");
        }
    };

    refreshButton?.addEventListener("click", refresh);
    window.setInterval(refresh, 10000);
})();
