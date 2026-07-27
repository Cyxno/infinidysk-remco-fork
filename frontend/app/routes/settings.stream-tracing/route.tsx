import { backendClient } from "~/clients/backend-client.server";

export async function loader() {
    const status = await backendClient.getStreamTracingStatus();
    return Response.json(status);
}

export async function action({ request }: { request: Request }) {
    const formData = await request.formData();
    if (formData.get("intent")?.toString() === "discard") {
        return Response.json(await backendClient.discardStreamTraces());
    }
    const enabled = formData.get("enabled")?.toString() === "true";
    const minutesRaw = Number(formData.get("minutes")?.toString() ?? "30");
    const minutes = [15, 30, 60].includes(minutesRaw) ? minutesRaw : 30;
    return Response.json(await backendClient.setStreamTracing(enabled, minutes));
}
