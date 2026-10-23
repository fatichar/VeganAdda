export async function resizeImageInput(inputId, maxBytes = 2 * 1024 * 1024, maxDimension = 1200) {
    const input = document.getElementById(inputId);
    if (!input || !input.files || !input.files[0]) {
        return null;
    }
    const file = input.files[0];
    if (file.size > 10 * 1024 * 1024) {
        throw new Error("Choose an image smaller than 10 MB.");
    }

    if (file.size <= maxBytes) {
        const buffer = await file.arrayBuffer();
        return {
            bytes: new Uint8Array(buffer),
            name: file.name,
            contentType: file.type || "image/jpeg"
        };
    }

    return new Promise((resolve, reject) => {
        const img = new Image();
        const url = URL.createObjectURL(file);
        img.onload = async () => {
            URL.revokeObjectURL(url);
            try {
                let width = img.width;
                let height = img.height;

                if (width > maxDimension || height > maxDimension) {
                    if (width > height) {
                        height = Math.round((height * maxDimension) / width);
                        width = maxDimension;
                    } else {
                        width = Math.round((width * maxDimension) / height);
                        height = maxDimension;
                    }
                }

                const canvas = document.createElement("canvas");
                canvas.width = Math.max(1, width);
                canvas.height = Math.max(1, height);
                const ctx = canvas.getContext("2d");
                if (!ctx) {
                    reject(new Error("Canvas context is not available."));
                    return;
                }

                ctx.fillStyle = "#FFFFFF";
                ctx.fillRect(0, 0, width, height);
                ctx.drawImage(img, 0, 0, width, height);

                let quality = 0.85;
                let blob = await new Promise((res) => canvas.toBlob(res, "image/jpeg", quality));

                while (blob && blob.size > maxBytes && quality > 0.3) {
                    quality -= 0.1;
                    blob = await new Promise((res) => canvas.toBlob(res, "image/jpeg", quality));
                }

                if (blob && blob.size > maxBytes) {
                    let curW = width;
                    let curH = height;
                    while (blob && blob.size > maxBytes && curW > 300) {
                        curW = Math.round(curW * 0.8);
                        curH = Math.round(curH * 0.8);
                        canvas.width = Math.max(1, curW);
                        canvas.height = Math.max(1, curH);
                        ctx.fillStyle = "#FFFFFF";
                        ctx.fillRect(0, 0, curW, curH);
                        ctx.drawImage(img, 0, 0, curW, curH);
                        blob = await new Promise((res) => canvas.toBlob(res, "image/jpeg", 0.75));
                    }
                }

                if (!blob || blob.size > maxBytes) {
                    reject(new Error("Unable to compress image below 2 MB limit."));
                    return;
                }

                const newName = file.name ? file.name.replace(/\.[^/.]+$/, "") + ".jpg" : "image.jpg";
                const buffer = await blob.arrayBuffer();
                resolve({
                    bytes: new Uint8Array(buffer),
                    name: newName,
                    contentType: "image/jpeg"
                });
            } catch (err) {
                reject(err);
            }
        };
        img.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error("This image could not be decoded."));
        };
        img.src = url;
    });
}
