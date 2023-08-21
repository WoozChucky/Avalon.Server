import axios from "axios";
import React from "react";

function CharacterTable() {

    const [account, setAccount] = React.useState(null);

    React.useEffect(() => {
        axios.get('https://localhost:7166/Account', {
            headers: {
                'Authorization': 'Bearer abc123'
            }
        }).then((response) => {
            console.log(response.data);
            setAccount(response.data);
        });
    }, []);

    if (!account) return null;

    return (
      <div>{(account as any).id}</div>
    );
}

export default CharacterTable;

