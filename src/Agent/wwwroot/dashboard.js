window.agentDashboard = {
    attachComposer(textareaId, buttonId) {
        const textarea = document.getElementById(textareaId);
        const button = document.getElementById(buttonId);

        if (!textarea || !button || textarea.dataset.agentComposerAttached === "true") {
            return;
        }

        textarea.dataset.agentComposerAttached = "true";
        textarea.addEventListener("keydown", (event) => {
            if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();

                if (!button.disabled) {
                    button.click();
                }
            }
        });
    },

    initCockpitSplit() {
        if (!window.Split || document.querySelectorAll(".split-root > .pane").length < 3) {
            return;
        }

        if (window.agentDashboard._split) {
            return;
        }

        window.agentDashboard._split = Split([".pane-chat", ".pane-context", ".pane-inspector"], {
            sizes: [52, 24, 24],
            minSize: [360, 260, 260],
            gutterSize: 10
        });
    },

    highlight() {
        if (!window.hljs) {
            return;
        }

        document.querySelectorAll("pre code:not([data-highlighted])").forEach((x) => {
            window.hljs.highlightElement(x);
        });
    },

    async renderMemoryGraphFromEndpoint(elementId, url) {
        const response = await fetch(url);

        if (!response.ok) {
            return;
        }

        const snapshot = await response.json();
        window.agentDashboard.renderMemoryGraph(elementId, snapshot);
    },

    renderMemoryGraph(elementId, snapshot) {
        const element = document.getElementById(elementId);

        if (!element || !window.cytoscape) {
            return;
        }

        const nodes = (snapshot.nodes || snapshot.Nodes || []).map((x) => ({
            data: {
                id: x.id || x.Id,
                label: x.label || x.Label,
                kind: x.kind || x.Kind
            }
        }));
        const edges = (snapshot.edges || snapshot.Edges || []).map((x) => ({
            data: {
                id: x.id || x.Id,
                source: x.sourceId || x.SourceId,
                target: x.targetId || x.TargetId,
                kind: x.kind || x.Kind
            }
        }));

        if (window.agentDashboard._graph) {
            window.agentDashboard._graph.destroy();
        }

        window.agentDashboard._graph = cytoscape({
            container: element,
            elements: [...nodes, ...edges],
            style: [
                {
                    selector: "node",
                    style: {
                        "background-color": "#146c72",
                        "border-width": 2,
                        "border-color": "#ffffff",
                        color: "#16202a",
                        label: "data(label)",
                        "font-size": 12,
                        "text-wrap": "wrap",
                        "text-max-width": 120,
                        "text-valign": "bottom",
                        "text-margin-y": 8
                    }
                },
                {
                    selector: "node[kind = 'segment']",
                    style: { "background-color": "#f5c15b" }
                },
                {
                    selector: "node[kind = 'tier']",
                    style: { "background-color": "#7396d1" }
                },
                {
                    selector: "node[kind = 'source']",
                    style: { "background-color": "#91b67f" }
                },
                {
                    selector: "edge",
                    style: {
                        width: 1.5,
                        "line-color": "#9aa8b2",
                        "target-arrow-color": "#9aa8b2",
                        "target-arrow-shape": "triangle",
                        "curve-style": "bezier"
                    }
                }
            ],
            layout: {
                name: "cose",
                animate: false,
                fit: true,
                padding: 40
            }
        });
    }
};
