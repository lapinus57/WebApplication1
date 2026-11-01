#!/usr/bin/env python3
"""Déchiffre un export chiffré EyeChat (.eyediag) : diagnostic SMS/mail ou post-it.

Usage : python3 lire_diagnostic.py fichier.eyediag [sortie.json]
Le mot de passe est demandé au clavier (ou variable EYEDIAG_PASSWORD).
Nécessite le paquet « cryptography » (pip install cryptography).
"""
import base64, getpass, gzip, hashlib, json, os, sys

from cryptography.hazmat.primitives.ciphers.aead import AESGCM


def lire(chemin, mot_de_passe):
    with open(chemin, "rb") as f:
        enveloppe = json.load(f)
    if enveloppe.get("format") not in ("eyechat-sms-diagnostic", "eyechat-postit-analysis"):
        raise SystemExit("Ce fichier n'est pas un export EyeChat chiffré.")
    b = lambda nom: base64.b64decode(enveloppe[nom])
    cle = hashlib.pbkdf2_hmac("sha256", mot_de_passe.encode("utf-8"), b("salt"), enveloppe["iterations"], 32)
    try:
        clair = AESGCM(cle).decrypt(b("nonce"), b("data") + b("tag"), enveloppe["aad"].encode("ascii"))
    except Exception:
        raise SystemExit("Mot de passe incorrect ou fichier modifié.")
    return json.loads(gzip.decompress(clair).decode("utf-8"))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    mot_de_passe = os.environ.get("EYEDIAG_PASSWORD") or getpass.getpass("Mot de passe : ")
    donnees = lire(sys.argv[1], mot_de_passe)
    texte = json.dumps(donnees, ensure_ascii=False, indent=2)
    if len(sys.argv) > 2:
        with open(sys.argv[2], "w", encoding="utf-8") as f:
            f.write(texte)
        if donnees.get("format") == "eyechat-postit-analysis":
            print(f"{donnees['keptPostIts']} post-it de consultation sur {donnees['totalPostIts']} -> {sys.argv[2]}")
        else:
            print(f"{len(donnees['appointments'])} RDV, {len(donnees['families'])} famille(s), "
                  f"{len(donnees['messages'])} message(s) -> {sys.argv[2]}")
    else:
        print(texte)
